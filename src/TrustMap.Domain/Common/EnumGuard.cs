namespace TrustMap.Domain.Common;

public static class EnumGuard
{
    public static void EnsureDefined<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be a defined {typeof(TEnum).Name} value.");
        }
    }
}
