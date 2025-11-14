using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

public class EnumToIntConverter<TEnum> : ValueConverter<TEnum, int>
    where TEnum : struct, Enum
{
    public EnumToIntConverter()
        : base(
            v => Convert.ToInt32(v),   
            v => (TEnum)Enum.ToObject(typeof(TEnum), v)) 
    {
    }
}
