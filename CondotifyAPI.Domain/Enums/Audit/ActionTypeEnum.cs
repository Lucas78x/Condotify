public enum ActionTypeEnum
{
    Create,
    Update,
    Delete
}
public static class ActionTypeMessages
{
    public static string Get(ActionTypeEnum action) =>
        action switch
        {
            ActionTypeEnum.Create => "Registro criado com sucesso",
            ActionTypeEnum.Update => "Registro atualizado com sucesso",
            ActionTypeEnum.Delete => "Registro removido com sucesso",
            _ => "Ação desconhecida"
        };
}