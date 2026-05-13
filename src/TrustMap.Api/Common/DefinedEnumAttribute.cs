using System.ComponentModel.DataAnnotations;

namespace TrustMap.Api.Common;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class DefinedEnumAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        var enumType = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (!enumType.IsEnum)
        {
            return new ValidationResult($"{validationContext.DisplayName} must be an enum value.");
        }

        return Enum.IsDefined(enumType, value)
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} must be one of the defined values.");
    }
}
