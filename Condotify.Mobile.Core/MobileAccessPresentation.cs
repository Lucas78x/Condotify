using Condotify.Models;

namespace Condotify.Mobile.Core;

public enum MobileVisitorView
{
    Today,
    Upcoming,
    History
}

public enum MobileAuditStatusKind
{
    Success,
    Pending,
    Alert,
    Neutral
}

public static class MobileAccessPresentation
{
    private static readonly HashSet<string> HistoricalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CheckedOut", "Denied", "Canceled", "Cancelled", "Expired"
    };

    private static readonly Dictionary<string, string> AuditActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Create"] = "Cadastro realizado",
        ["Created"] = "Cadastro realizado",
        ["Update"] = "Dados atualizados",
        ["Updated"] = "Dados atualizados",
        ["Delete"] = "Registro removido",
        ["Deleted"] = "Registro removido",
        ["Purged"] = "Registro removido definitivamente",
        ["Inspect"] = "Verificação realizada",
        ["Inspected"] = "Verificação realizada",
        ["Reconcile"] = "Conciliação realizada",
        ["Reconciled"] = "Conciliação realizada",
        ["Issued"] = "Emissão realizada",
        ["VisitCreated"] = "Visitante autorizado",
        ["VisitQrScanned"] = "Entrada por QR Code",
        ["VisitStatus"] = "Movimentação de visitante",
        ["CredentialStatus"] = "Situação da credencial alterada",
        ["OpenDoor"] = "Porta acionada",
        ["OfflineDeviceRegistered"] = "Operação offline registrada",
        ["DeviceOffline"] = "Equipamento ficou offline",
        ["DeviceRecovered"] = "Conexão do equipamento restabelecida",
        ["ProvisionCredential"] = "Credencial enviada ao equipamento",
        ["ActivateCredential"] = "Credencial ativada",
        ["DeactivateCredential"] = "Credencial suspensa",
        ["RestoreCredential"] = "Credencial restaurada",
        ["RemoveCredential"] = "Credencial removida do equipamento",
        ["RenewCredential"] = "QR Code renovado",
        ["FaceEnrollment"] = "Cadastro facial iniciado",
        ["ReadAccessLogs"] = "Histórico de acesso consultado",
        ["ArchiveGenerated"] = "Arquivo de configuração gerado",
        ["RuleCreated"] = "Regra criada",
        ["RuleUpdated"] = "Regra atualizada",
        ["RuleDeleted"] = "Regra removida",
        ["Tested"] = "Teste realizado"
    };

    private static readonly Dictionary<string, string> AuditEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Visit"] = "Visitante",
        ["Resident"] = "Pessoa",
        ["Credential"] = "Credencial",
        ["Device"] = "Equipamento",
        ["CftvDevice"] = "Câmera",
        ["AccessRoute"] = "Rota de acesso",
        ["DigitalPass"] = "Passe digital",
        ["OfflineOperation"] = "Operação offline",
        ["RegistrationInvite"] = "Convite de cadastro",
        ["AccessBatch"] = "Lote de operações",
        ["AlertNotification"] = "Notificação de alerta",
        ["OperationalAlert"] = "Alerta operacional",
        ["AutomationRule"] = "Regra de automação",
        ["BackupAutomation"] = "Rotina de backup",
        ["ConfigurationBackup"] = "Backup de configuração",
        ["EmergencySession"] = "Emergência",
        ["Incident"] = "Ocorrência",
        ["WorkOrder"] = "Ordem de serviço",
        ["FinancialAutomation"] = "Automação financeira",
        ["FinancialCharge"] = "Cobrança",
        ["WalletIntegration"] = "Carteira digital",
        ["AssistedMigration"] = "Migração assistida",
        ["Unit"] = "Unidade",
        ["Block"] = "Bloco"
    };

    private static readonly Dictionary<string, string> AuditStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Success"] = "Concluído",
        ["Succeeded"] = "Concluído",
        ["Completed"] = "Concluído",
        ["Recorded"] = "Registrado",
        ["Recovered"] = "Restabelecido",
        ["Synced"] = "Sincronizado",
        ["Pending"] = "Aguardando",
        ["PendingEnrollment"] = "Aguardando cadastro facial",
        ["Queued"] = "Na fila",
        ["Running"] = "Em processamento",
        ["Processing"] = "Em processamento",
        ["WaitingDevice"] = "Aguardando equipamento",
        ["Alert"] = "Requer atenção",
        ["Offline"] = "Sem conexão",
        ["Failed"] = "Falhou",
        ["Error"] = "Falhou",
        ["Denied"] = "Negado",
        ["Rejected"] = "Rejeitado",
        ["Canceled"] = "Cancelado",
        ["Cancelled"] = "Cancelado",
        ["DeadLetter"] = "Não processado",
        ["Expired"] = "Expirado"
    };

    public static bool MatchesVisitorView(ConciergeVisitViewModel visit, MobileVisitorView view, DateTime now)
    {
        var from = ToLocal(visit.ValidFrom);
        var to = ToLocal(visit.ValidTo);
        var historical = HistoricalStatuses.Contains(visit.Status) || to < now;

        return view switch
        {
            MobileVisitorView.Today => !historical && from.Date <= now.Date && to.Date >= now.Date,
            MobileVisitorView.Upcoming => !historical && from.Date > now.Date,
            MobileVisitorView.History => historical,
            _ => false
        };
    }

    public static string NormalizeTagHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = new string(value.Where(character => !char.IsWhiteSpace(character) && character is not '-' and not ':').ToArray());
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..];
        return normalized.ToUpperInvariant();
    }

    public static string ValidateTagHex(string? value)
    {
        var normalized = NormalizeTagHex(value);
        if (normalized.Length == 0) return string.Empty;
        if (normalized.Length is < 6 or > 64) return "O código hexadecimal deve ter entre 6 e 64 caracteres.";
        if (normalized.Length % 2 != 0) return "O código hexadecimal precisa ter pares completos de caracteres.";
        if (normalized.Any(character => !Uri.IsHexDigit(character))) return "Use somente números de 0 a 9 e letras de A a F.";
        return string.Empty;
    }

    public static string AuditActionLabel(string? action, string? entityType = null)
    {
        if (string.IsNullOrWhiteSpace(action)) return "Operação registrada";
        if (action.Equals("Issued", StringComparison.OrdinalIgnoreCase) &&
            entityType?.Equals("DigitalPass", StringComparison.OrdinalIgnoreCase) == true)
            return "Passe digital emitido";

        return AuditActions.GetValueOrDefault(action.Trim(), "Operação registrada");
    }

    public static string AuditEntityLabel(string? entityType) =>
        string.IsNullOrWhiteSpace(entityType)
            ? "Sistema"
            : AuditEntities.GetValueOrDefault(entityType.Trim(), "Sistema");

    public static string AuditStatusLabel(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? "Registrado"
            : AuditStatuses.GetValueOrDefault(status.Trim(), "Registrado");

    public static MobileAuditStatusKind AuditStatusKind(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return MobileAuditStatusKind.Neutral;
        var normalized = status.Trim();

        if (ContainsAny(normalized, "fail", "error", "alert", "offline", "denied", "reject", "cancel", "dead", "expired"))
            return MobileAuditStatusKind.Alert;
        if (ContainsAny(normalized, "pending", "queued", "running", "processing", "waiting"))
            return MobileAuditStatusKind.Pending;
        if (ContainsAny(normalized, "success", "succeed", "completed", "recorded", "recovered", "synced"))
            return MobileAuditStatusKind.Success;

        return MobileAuditStatusKind.Neutral;
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static DateTime ToLocal(DateTime value) => value.Kind == DateTimeKind.Local ? value : value.ToLocalTime();
}
