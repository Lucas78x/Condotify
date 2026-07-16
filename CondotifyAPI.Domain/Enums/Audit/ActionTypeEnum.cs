public enum ActionTypeEnum
{
    Create = 0,
    Update = 1,
    Delete = 2,
    OpenDoor = 3,
    ProvisionCredential = 4,
    ActivateCredential = 5,
    DeactivateCredential = 6,
    RestoreCredential = 7,
    RemoveCredential = 8,
    FaceEnrollment = 9,
    ReadAccessLogs = 10,
    RenewCredential = 11
}
public static class ActionTypeMessages
{
    public static string Get(ActionTypeEnum action) =>
        action switch
        {
            ActionTypeEnum.Create => "Registro criado com sucesso",
            ActionTypeEnum.Update => "Registro atualizado com sucesso",
            ActionTypeEnum.Delete => "Registro removido com sucesso",
            ActionTypeEnum.OpenDoor => "Porta acionada remotamente",
            ActionTypeEnum.ProvisionCredential => "Credencial provisionada",
            ActionTypeEnum.ActivateCredential => "Credencial ativada",
            ActionTypeEnum.DeactivateCredential => "Credencial suspensa",
            ActionTypeEnum.RestoreCredential => "Credencial restaurada",
            ActionTypeEnum.RenewCredential => "QR Code renovado",
            ActionTypeEnum.RemoveCredential => "Credencial removida do equipamento",
            ActionTypeEnum.FaceEnrollment => "Cadastro facial iniciado",
            ActionTypeEnum.ReadAccessLogs => "Logs de acesso consultados",
            _ => "Ação desconhecida"
        };
}
