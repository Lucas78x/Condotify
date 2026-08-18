namespace Condotify.Mobile.Services;

#if ANDROID
#pragma warning disable CA1416
#pragma warning disable CA1422
#endif

public sealed class MobileBiometricService
{
    public Task<bool> IsAvailableAsync()
    {
#if ANDROID
        if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.P) return Task.FromResult(false);
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var keyguard = activity?.GetSystemService(Android.Content.Context.KeyguardService) as Android.App.KeyguardManager;
        return Task.FromResult(keyguard?.IsDeviceSecure == true);
#else
        return Task.FromResult(false);
#endif
    }

    public async Task<bool> AuthenticateAsync(string reason)
    {
#if ANDROID
        if (!await IsAvailableAsync()) return false;
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null) return false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellation = new Android.OS.CancellationSignal();
        var executor = activity.MainExecutor!;
        var builder = new Android.Hardware.Biometrics.BiometricPrompt.Builder(activity)
            .SetTitle("Desbloquear F&F Access")
            .SetSubtitle(reason);
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
            builder.SetDeviceCredentialAllowed(true);
        else
            builder.SetNegativeButton("Cancelar", executor, new NegativeListener(completion, cancellation));
        var prompt = builder.Build();
        prompt.Authenticate(cancellation, executor, new AuthenticationListener(completion));
        return await completion.Task;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

#if ANDROID
    private sealed class AuthenticationListener(TaskCompletionSource<bool> completion) : Android.Hardware.Biometrics.BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(Android.Hardware.Biometrics.BiometricPrompt.AuthenticationResult? result)
        {
            completion.TrySetResult(true);
            base.OnAuthenticationSucceeded(result);
        }
        public override void OnAuthenticationError(Android.Hardware.Biometrics.BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString)
        {
            completion.TrySetResult(false);
            base.OnAuthenticationError(errorCode, errString);
        }
    }

    private sealed class NegativeListener(TaskCompletionSource<bool> completion, Android.OS.CancellationSignal cancellation) : Java.Lang.Object, Android.Content.IDialogInterfaceOnClickListener
    {
        public void OnClick(Android.Content.IDialogInterface? dialog, int which)
        {
            cancellation.Cancel();
            completion.TrySetResult(false);
        }
    }
#endif
}
