using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Condotify.Mobile.Core;
using Condotify.Models;
using Condotify.Services;

namespace Condotify.Mobile.Services;

public sealed record MobileQrScanOutcome(
    bool Success,
    bool WasOffline,
    string Message,
    ConciergeVisitViewModel? Visit = null,
    OfflineAccessDecisionCode? DecisionCode = null);

public sealed class MobileOfflineOperationsService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(2);
    private readonly CondotifyApiClient _api;
    private readonly MobileSessionCoordinator _session;
    private readonly MobileAppState _appState;
    private readonly MobileDeviceContext _device;
    private readonly MobileConnectivityState _network;
    private readonly MobileOfflineProtectedStore _store;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private MobileOfflineDatabase _database = new();
    private long _databaseEpoch;
    private bool _initialized;
    private bool _disposed;

    public MobileOfflineOperationsService(
        CondotifyApiClient api,
        MobileSessionCoordinator session,
        MobileAppState appState,
        MobileDeviceContext device,
        MobileConnectivityState network,
        MobileOfflineProtectedStore store)
    {
        _api = api;
        _session = session;
        _appState = appState;
        _device = device;
        _network = network;
        _store = store;
        _network.Changed += OnNetworkChanged;
        _appState.Changed += OnAppStateChanged;
    }

    public event Action? Changed;
    public bool IsSyncing { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public OfflineDeviceStatus? DeviceStatus => CurrentState?.Device?.Status;
    public DateTime? LastSyncedAt => CurrentState?.Device?.LastSyncedAt;
    public DateTime? BundleExpiresAt => CurrentState?.Bundle?.ExpiresAt;
    public int PendingCount => CurrentState?.Outbox.Count ?? 0;
    public int ConflictCount => CurrentState?.RecentResults.Count(x => x.Status is OfflineOperationStatus.Conflict or OfflineOperationStatus.Rejected) ?? 0;
    public bool CanOperateOffline
    {
        get
        {
            var state = CurrentState;
            return PrepareOfflineDecision(state).Error is null;
        }
    }

    public string AvailabilityMessage => DeviceStatus switch
    {
        OfflineDeviceStatus.Pending => "Este aparelho aguarda aprovação na plataforma.",
        OfflineDeviceStatus.Revoked => "A operação offline foi revogada para este aparelho.",
        OfflineDeviceStatus.Approved when BundleExpiresAt.HasValue && !CanOperateOffline => "A base segura venceu e precisa ser sincronizada.",
        OfflineDeviceStatus.Approved => "Base offline protegida e pronta para uso.",
        _ => "Conecte-se para registrar este aparelho na operação offline."
    };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        NotifyChanged();
        if (_network.IsOnline && IsStaffSession) _ = TrySyncInBackgroundAsync(cancellationToken);
    }

    public async Task<bool> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !_network.IsOnline || !IsStaffSession || !_appState.SelectedLicenseId.HasValue) return false;
        await EnsureInitializedAsync(cancellationToken);
        if (!await _syncGate.WaitAsync(0, cancellationToken)) return false;
        IsSyncing = true;
        LastError = string.Empty;
        NotifyChanged();
        var syncLicenseId = _appState.SelectedLicenseId;
        try
        {
            if (!syncLicenseId.HasValue) return false;
            var licenseId = syncLicenseId.Value;
            var userId = CurrentUserId;
            if (!userId.HasValue) return false;
            var syncEpoch = Volatile.Read(ref _databaseEpoch);
            List<OfflineOperationUploadViewModel> queued;
            await _stateGate.WaitAsync(cancellationToken);
            try
            {
                if (syncEpoch != Volatile.Read(ref _databaseEpoch) ||
                    CurrentUserId != userId ||
                    _appState.SelectedLicenseId != licenseId)
                    return false;

                var state = GetOrCreateState(licenseId);
                EnsureOwner(state);
                queued = state.Outbox.Select(CloneOperation).ToList();
            }
            finally { _stateGate.Release(); }

            var request = new OfflineSyncRequestViewModel
            {
                InstallationId = _device.InstallationId,
                DeviceName = _device.DeviceLabel,
                Platform = DeviceInfo.Current.Platform.ToString(),
                AppVersion = _device.AppVersion,
                Operations = queued
            };
            var result = await _api.SyncOfflineOperationsAsync(licenseId, request, cancellationToken);
            if (!result.Success || result.Value is null)
            {
                LastError = result.Error ?? "Não foi possível sincronizar a operação offline.";
                return false;
            }

            await _stateGate.WaitAsync(cancellationToken);
            try
            {
                if (syncEpoch != Volatile.Read(ref _databaseEpoch) ||
                    CurrentUserId != userId ||
                    _appState.SelectedLicenseId != licenseId)
                    return false;

                var state = GetOrCreateState(licenseId);
                EnsureOwner(state);
                state.Device = result.Value.Device;
                if (!string.IsNullOrWhiteSpace(result.Value.Device.DeviceSecret))
                    state.DeviceSecret = result.Value.Device.DeviceSecret;

                var completedIds = result.Value.Operations.Select(x => x.ClientOperationId).ToHashSet();
                state.Outbox.RemoveAll(x => completedIds.Contains(x.ClientOperationId));
                state.RecentResults.InsertRange(0, result.Value.Operations.OrderByDescending(x => x.ReceivedAt));
                state.RecentResults = state.RecentResults
                    .GroupBy(x => x.ClientOperationId).Select(x => x.First())
                    .OrderByDescending(x => x.ReceivedAt).Take(80).ToList();

                if (result.Value.Device.Status != OfflineDeviceStatus.Approved)
                {
                    state.DeviceSecret = string.Empty;
                    state.Bundle = null;
                    state.BundleEnvelope = null;
                }
                else if (result.Value.Bundle is not null)
                {
                    if (string.IsNullOrWhiteSpace(state.DeviceSecret) ||
                        !OfflineBundleAuthenticator.Verify(result.Value.Bundle, state.DeviceSecret) ||
                        !TryDecodeBundle(result.Value.Bundle, out var payload) ||
                        payload.DeviceId != result.Value.Device.Id || payload.LicenseId != licenseId)
                    {
                        LastError = "A API enviou uma base offline com assinatura inválida. A base anterior foi preservada.";
                    }
                    else
                    {
                        state.BundleEnvelope = result.Value.Bundle;
                        state.Bundle = payload;
                        state.BundleReceivedAtLocalUtc = DateTime.UtcNow;
                        state.LastTrustedUtc = payload.ServerTime;
                        ReplayOutbox(state);
                    }
                }

                await _store.SaveAsync(_database, cancellationToken);
            }
            finally { _stateGate.Release(); }
            return string.IsNullOrWhiteSpace(LastError);
        }
        finally
        {
            IsSyncing = false;
            _syncGate.Release();
            NotifyChanged();
            if (_network.IsOnline && IsStaffSession &&
                _appState.SelectedLicenseId.HasValue &&
                _appState.SelectedLicenseId != syncLicenseId)
                _ = TrySyncInBackgroundAsync(CancellationToken.None);
        }
    }

    public async Task<MobileQrScanOutcome> ScanQrAsync(
        Guid licenseId,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (_network.IsOnline)
        {
            var online = await _api.ScanConciergeVisitAsync(licenseId, code.Trim(), cancellationToken);
            if (online.Success && online.Value is not null)
            {
                _ = TrySyncInBackgroundAsync(CancellationToken.None);
                return new MobileQrScanOutcome(true, false, "Entrada validada online.", online.Value);
            }

            if (!CanFallbackFrom(online.StatusCode))
                return new MobileQrScanOutcome(false, false, online.Error ?? "O convite não pôde ser validado.");
        }

        await EnsureInitializedAsync(cancellationToken);
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var state = GetOwnedState(licenseId);
            var preparation = PrepareOfflineDecision(state);
            if (preparation.Error is not null)
                return new MobileQrScanOutcome(false, true, preparation.Error, DecisionCode: preparation.Code);

            var decision = OfflineAccessEvaluator.Evaluate(preparation.Payload, code, preparation.TrustedUtc, preparation.ClockRollback);
            if (!decision.Allowed || decision.Permit is null)
                return new MobileQrScanOutcome(false, true, decision.Message, DecisionCode: decision.Code);

            var permit = decision.Permit;
            var operation = new OfflineOperationUploadViewModel
            {
                ClientOperationId = Guid.NewGuid(), BundleId = preparation.Payload!.BundleId,
                Kind = OfflineOperationKind.VisitCheckIn, VisitId = permit.VisitId,
                CodeHash = permit.CodeHash, OccurredAt = preparation.TrustedUtc
            };
            state!.Outbox.Add(operation);
            permit.UseCount++;
            permit.Status = "CheckedIn";
            state.LastTrustedUtc = preparation.TrustedUtc;
            await _store.SaveAsync(_database, cancellationToken);
            NotifyChanged();
            return new MobileQrScanOutcome(
                true,
                true,
                "Entrada autorizada offline. O registro será sincronizado automaticamente.",
                ToVisit(permit),
                OfflineAccessDecisionCode.Allowed);
        }
        finally { _stateGate.Release(); }
    }

    public async Task<MobileQrScanOutcome> QueueVisitOperationAsync(
        Guid licenseId,
        Guid visitId,
        OfflineOperationKind kind,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var state = GetOwnedState(licenseId);
            var preparation = PrepareOfflineDecision(state);
            if (preparation.Error is not null)
                return new MobileQrScanOutcome(false, true, preparation.Error, DecisionCode: preparation.Code);
            var permit = preparation.Payload!.Visits.FirstOrDefault(x => x.VisitId == visitId);
            if (permit is null)
                return new MobileQrScanOutcome(false, true, "Esta visita não está disponível na base offline.");

            if (kind == OfflineOperationKind.VisitCheckIn)
            {
                if (!permit.Status.Equals("Scheduled", StringComparison.OrdinalIgnoreCase))
                    return new MobileQrScanOutcome(false, true, "A entrada desta visita não está mais pendente.");
                if (preparation.TrustedUtc < permit.ValidFrom || preparation.TrustedUtc > permit.ValidTo)
                    return new MobileQrScanOutcome(false, true, "A visita está fora do período autorizado.");
                if (permit.MaxUses.HasValue && permit.UseCount >= permit.MaxUses.Value)
                    return new MobileQrScanOutcome(false, true, "O limite de utilizações foi atingido.");
                if (permit.MaxUses.HasValue && permit.MaxUses.Value - permit.UseCount <= 1 && !preparation.Payload.IsPrimaryValidator)
                    return new MobileQrScanOutcome(false, true, "O último uso exige o validador principal.");
                if (!OfflineRouteSchedule.IsAllowed(permit.Routes, preparation.TrustedUtc, preparation.Payload.UtcOffsetMinutes))
                    return new MobileQrScanOutcome(false, true, "A visita está fora dos horários permitidos.");
                permit.UseCount++;
                permit.Status = "CheckedIn";
            }
            else
            {
                if (!permit.Status.Equals("CheckedIn", StringComparison.OrdinalIgnoreCase))
                    return new MobileQrScanOutcome(false, true, "Não há entrada aberta para registrar a saída.");
                permit.Status = "CheckedOut";
            }

            state!.Outbox.Add(new OfflineOperationUploadViewModel
            {
                ClientOperationId = Guid.NewGuid(), BundleId = preparation.Payload.BundleId,
                Kind = kind, VisitId = permit.VisitId, CodeHash = permit.CodeHash, OccurredAt = preparation.TrustedUtc
            });
            state.LastTrustedUtc = preparation.TrustedUtc;
            await _store.SaveAsync(_database, cancellationToken);
            NotifyChanged();
            return new MobileQrScanOutcome(true, true,
                kind == OfflineOperationKind.VisitCheckIn
                    ? "Entrada registrada offline e adicionada à fila."
                    : "Saída registrada offline e adicionada à fila.",
                ToVisit(permit));
        }
        finally { _stateGate.Release(); }
    }

    public async Task<ConciergeDashboardViewModel?> GetOfflineDashboardAsync(
        Guid licenseId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var state = GetOwnedState(licenseId);
            var preparation = PrepareOfflineDecision(state);
            if (preparation.Error is not null || preparation.Payload is null) return null;
            ReplayOutbox(preparation.State!);
            var trustedUtc = preparation.TrustedUtc;
            var localDate = trustedUtc.AddMinutes(preparation.Payload.UtcOffsetMinutes).Date;
            var visits = preparation.Payload.Visits.Select(ToVisit).OrderBy(x => x.ValidFrom).ToList();
            return new ConciergeDashboardViewModel
            {
                Visits = visits,
                ExpectedToday = preparation.Payload.Visits.Count(x => x.Status.Equals("Scheduled", StringComparison.OrdinalIgnoreCase) &&
                    x.ValidFrom.AddMinutes(preparation.Payload.UtcOffsetMinutes).Date <= localDate &&
                    x.ValidTo.AddMinutes(preparation.Payload.UtcOffsetMinutes).Date >= localDate),
                InsideNow = preparation.Payload.Visits.Count(x => x.Status.Equals("CheckedIn", StringComparison.OrdinalIgnoreCase)),
                PendingApprovals = 0,
                OfflineDevices = 0
            };
        }
        finally { _stateGate.Release(); }
    }

    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            _database = new MobileOfflineDatabase();
            Interlocked.Increment(ref _databaseEpoch);
            _initialized = true;
            LastError = string.Empty;
            await _store.ClearAsync(cancellationToken);
        }
        finally { _stateGate.Release(); }
        NotifyChanged();
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            _database = await _store.LoadAsync(cancellationToken);
            _initialized = true;
        }
        finally { _stateGate.Release(); }
    }

    private (MobileOfflineLicenseState? State, OfflineAccessBundlePayloadViewModel? Payload, DateTime TrustedUtc, bool ClockRollback, string? Error, OfflineAccessDecisionCode? Code)
        PrepareOfflineDecision(MobileOfflineLicenseState? state)
    {
        if (state is null || !MobileOfflineOwnership.BelongsToCurrentSession(state.UserId, CurrentUserId))
            return (state, null, default, false, "Nenhuma base offline protegida está disponível para esta sessão.", OfflineAccessDecisionCode.InvalidBundle);
        if (state?.Device?.Status != OfflineDeviceStatus.Approved)
            return (state, null, default, false, state?.Device?.Status == OfflineDeviceStatus.Revoked
                ? "A operação offline foi revogada para este aparelho."
                : "Este aparelho ainda não está aprovado para operação offline.", OfflineAccessDecisionCode.InvalidBundle);
        if (state.Bundle is null || state.BundleEnvelope is null || string.IsNullOrWhiteSpace(state.DeviceSecret))
            return (state, null, default, false, "Nenhuma base offline protegida foi sincronizada.", OfflineAccessDecisionCode.InvalidBundle);
        if (state.Bundle.LicenseId != state.LicenseId ||
            state.Device is null ||
            state.Bundle.DeviceId != state.Device.Id)
            return (state, null, default, false, "A base offline não pertence a este aparelho ou condomínio.", OfflineAccessDecisionCode.InvalidBundle);
        if (!OfflineBundleAuthenticator.Verify(state.BundleEnvelope, state.DeviceSecret))
            return (state, null, default, false, "A assinatura da base offline é inválida. Conecte-se antes de operar.", OfflineAccessDecisionCode.InvalidBundle);
        var trustedUtc = EstimateTrustedUtc(state, out var rollback);
        if (rollback)
            return (state, null, trustedUtc, true, "O relógio do aparelho mudou. Conecte-se para renovar a base segura.", OfflineAccessDecisionCode.ClockInvalid);
        if (trustedUtc < state.Bundle.GeneratedAt.Add(-ClockRollbackTolerance))
            return (state, null, trustedUtc, false, "A base offline ainda não está dentro da janela confiável.", OfflineAccessDecisionCode.BundleNotActive);
        if (trustedUtc > state.Bundle.ExpiresAt)
            return (state, null, trustedUtc, false, "A base offline venceu. Restabeleça a conexão para sincronizar.", OfflineAccessDecisionCode.BundleExpired);
        return (state, state.Bundle, trustedUtc, rollback, null, null);
    }

    private static DateTime EstimateTrustedUtc(MobileOfflineLicenseState state, out bool rollback)
    {
        if (state.Bundle is null)
        {
            rollback = false;
            return DateTime.UtcNow;
        }
        var elapsed = DateTime.UtcNow - state.BundleReceivedAtLocalUtc;
        var estimated = state.Bundle.ServerTime + elapsed;
        rollback = elapsed < -ClockRollbackTolerance ||
                   (state.LastTrustedUtc != default && estimated < state.LastTrustedUtc - ClockRollbackTolerance);
        return estimated.Kind == DateTimeKind.Utc ? estimated : DateTime.SpecifyKind(estimated, DateTimeKind.Utc);
    }

    private MobileOfflineLicenseState GetOrCreateState(Guid licenseId)
    {
        var key = licenseId.ToString("N");
        if (!_database.Licenses.TryGetValue(key, out var state))
        {
            state = new MobileOfflineLicenseState { LicenseId = licenseId, UserId = _session.Current?.SubjectId ?? Guid.Empty };
            _database.Licenses[key] = state;
        }
        return state;
    }

    private MobileOfflineLicenseState? GetOwnedState(Guid licenseId)
    {
        var state = _database.Licenses.GetValueOrDefault(licenseId.ToString("N"));
        return state is not null && MobileOfflineOwnership.BelongsToCurrentSession(state.UserId, CurrentUserId)
            ? state
            : null;
    }

    private void EnsureOwner(MobileOfflineLicenseState state)
    {
        var userId = CurrentUserId ?? Guid.Empty;
        if (state.UserId == userId) return;
        state.UserId = userId;
        state.Device = null;
        state.DeviceSecret = string.Empty;
        state.Bundle = null;
        state.BundleEnvelope = null;
        state.Outbox.Clear();
        state.RecentResults.Clear();
    }

    private MobileOfflineLicenseState? CurrentState =>
        _appState.SelectedLicenseId.HasValue ? GetOwnedState(_appState.SelectedLicenseId.Value) : null;

    private Guid? CurrentUserId => _session.IsAuthenticated && _session.Current?.SubjectId is { } userId && userId != Guid.Empty
        ? userId
        : null;

    private bool IsStaffSession => _session.IsAuthenticated && _session.Current?.Principal == MobilePrincipalKind.Staff;

    private static bool CanFallbackFrom(HttpStatusCode? statusCode) =>
        !statusCode.HasValue || (int)statusCode.Value >= 500 || statusCode == HttpStatusCode.RequestTimeout;

    private static OfflineOperationUploadViewModel CloneOperation(OfflineOperationUploadViewModel item) => new()
    {
        ClientOperationId = item.ClientOperationId, BundleId = item.BundleId, Kind = item.Kind,
        VisitId = item.VisitId, CodeHash = item.CodeHash, OccurredAt = item.OccurredAt
    };

    private static void ReplayOutbox(MobileOfflineLicenseState state)
    {
        if (state.Bundle is null) return;
        foreach (var operation in state.Outbox.OrderBy(x => x.OccurredAt))
        {
            var permit = state.Bundle.Visits.FirstOrDefault(x => x.VisitId == operation.VisitId);
            if (permit is null) continue;
            if (operation.Kind == OfflineOperationKind.VisitCheckIn && !permit.Status.Equals("CheckedIn", StringComparison.OrdinalIgnoreCase))
            {
                permit.Status = "CheckedIn";
                permit.UseCount++;
            }
            else if (operation.Kind == OfflineOperationKind.VisitCheckOut)
                permit.Status = "CheckedOut";
        }
    }

    private static ConciergeVisitViewModel ToVisit(OfflineVisitPermitViewModel permit) => new()
    {
        Id = permit.VisitId, HostName = permit.HostName, BlockName = permit.BlockName, UnitNumber = permit.UnitNumber,
        VisitorName = permit.VisitorName, Purpose = permit.Purpose, VehiclePlate = permit.VehiclePlate,
        Status = permit.Status, CredentialType = "QrCode", UseCount = permit.UseCount, MaxUses = permit.MaxUses,
        ValidFrom = permit.ValidFrom, ValidTo = permit.ValidTo
    };

    private static bool TryDecodeBundle(OfflineAccessBundleEnvelopeViewModel envelope, out OfflineAccessBundlePayloadViewModel payload)
    {
        payload = null!;
        try
        {
            var bytes = Convert.FromBase64String(envelope.PayloadBase64);
            payload = JsonSerializer.Deserialize<OfflineAccessBundlePayloadViewModel>(bytes, JsonOptions)!;
            return payload is not null && payload.SchemaVersion == 1;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return false;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private void OnNetworkChanged()
    {
        NotifyChanged();
        if (_network.IsOnline) _ = TrySyncInBackgroundAsync(CancellationToken.None);
    }

    private void OnAppStateChanged()
    {
        NotifyChanged();
        if (_network.IsOnline) _ = TrySyncInBackgroundAsync(CancellationToken.None);
    }

    private async Task TrySyncInBackgroundAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SyncAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            LastError = "Não foi possível concluir a sincronização offline.";
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        try { Changed?.Invoke(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _network.Changed -= OnNetworkChanged;
        _appState.Changed -= OnAppStateChanged;
        _stateGate.Dispose();
        _syncGate.Dispose();
    }
}
