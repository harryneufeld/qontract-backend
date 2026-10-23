using FluentValidation;
using Qontract.Application.Models;
using Qontract.Core.Security;
using Qontract.Domain.Models;

namespace Qontract.Application.FluentValidation;

public class TenantCreateValidator : AbstractValidator<TenantCreateDTO>
{
    public TenantCreateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Invalid tenant type.");
    }
}

public class TenantUpdateValidator : AbstractValidator<TenantUpdateDTO>
{
    public TenantUpdateValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(x => x.Type)
            .IsInEnum().When(x => x.Type.HasValue).WithMessage("Invalid tenant type.");

        RuleFor(x => x.Settings)
            .MaximumLength(5000).WithMessage("Settings cannot exceed 5000 characters.");

        RuleFor(x => x.IsActive)
            .Cascade(CascadeMode.Stop);
    }
}

public class ChangePasswordValidator : AbstractValidator<ChangePasswordDTO>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(PasswordPolicy.MinimumLength).WithMessage($"New password must be at least {PasswordPolicy.MinimumLength} characters.")
            .MaximumLength(PasswordPolicy.MaximumLength).WithMessage($"Password cannot exceed {PasswordPolicy.MaximumLength} characters.");
    }
}

public class UserUpdateValidator : AbstractValidator<UserUpdateDTO>
{
    public UserUpdateValidator()
    {
        RuleFor(x => x.Role)
            .Must(r => r is null or UserRole.TenantAdmin or UserRole.User).WithMessage("Invalid role.");

        RuleFor(x => x.IsActive)
            .Cascade(CascadeMode.Stop);
    }
}

public class ContractorCreateValidator : AbstractValidator<ContractorCreateDTO>
{
    public ContractorCreateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage($"Type must be {nameof(ContractorType.Individual)} or {nameof(ContractorType.Company)}.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email)).WithMessage("Invalid email format.")
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.Email)).WithMessage("Email cannot exceed 200 characters.");

        RuleFor(x => x.Phone)
            .MaximumLength(50).When(x => !string.IsNullOrEmpty(x.Phone)).WithMessage("Phone cannot exceed 50 characters.");
    }
}

public class ContractorUpdateValidator : AbstractValidator<ContractorUpdateDTO>
{
    public ContractorUpdateValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).When(x => x.Name is not null).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(x => x.Type)
            .IsInEnum().When(x => x.Type.HasValue).WithMessage($"Type must be {nameof(ContractorType.Individual)} or {nameof(ContractorType.Company)}.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => x.Email is not null && !string.IsNullOrEmpty(x.Email)).WithMessage("Invalid email format.")
            .MaximumLength(200).When(x => x.Email is not null && !string.IsNullOrEmpty(x.Email)).WithMessage("Email cannot exceed 200 characters.");

        RuleFor(x => x.Phone)
            .MaximumLength(50).When(x => x.Phone is not null && !string.IsNullOrEmpty(x.Phone)).WithMessage("Phone cannot exceed 50 characters.");
    }
}

// ---------------------------------------------------------------------
// Contract validators (issue #39): previously these DTOs relied on
// [ApiController] DataAnnotations auto-validation only; now every
// mutation endpoint validates through a DI-registered IValidator<T>.
// ---------------------------------------------------------------------

public class ContractCreateValidator : AbstractValidator<ContractCreateDTO>
{
    public ContractCreateValidator()
    {
        RuleFor(x => x.NoticePeriodUnit).IsInEnum().When(x => x.NoticePeriodUnit.HasValue);
        RuleFor(x => x.NoticePeriodValue).InclusiveBetween(1, 3650).When(x => x.NoticePeriodValue.HasValue)
            .WithMessage("Notice period must be between 1 and 3650.");
        RuleFor(x => x.RenewalMonths).InclusiveBetween(1, 120).When(x => x.RenewalMonths.HasValue)
            .WithMessage("Renewal period must be between 1 and 120 months.");
        RuleFor(x => x.Type).IsInEnum().When(x => x.Type.HasValue);
        RuleFor(x => x.BillingPeriod).IsInEnum().When(x => x.BillingPeriod.HasValue);
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).When(x => x.Description is not null).WithMessage("Description cannot exceed 2000 characters.");

        RuleFor(x => x.Status)
            .IsInEnum().WithMessage($"Status must be {nameof(ContractStatus.Draft)}, {nameof(ContractStatus.Active)}, {nameof(ContractStatus.Expired)} or {nameof(ContractStatus.Terminated)}.");

        RuleFor(x => x.StartDate)
            .MaximumLength(50).When(x => x.StartDate is not null).WithMessage("StartDate cannot exceed 50 characters.");

        RuleFor(x => x.EndDate)
            .MaximumLength(50).When(x => x.EndDate is not null).WithMessage("EndDate cannot exceed 50 characters.");

        RuleFor(x => x.Currency)
            .MaximumLength(50).When(x => x.Currency is not null).WithMessage("Currency cannot exceed 50 characters.");

        RuleFor(x => x.Value)
            .MaximumLength(500).When(x => x.Value is not null).WithMessage("Value cannot exceed 500 characters.");

        RuleFor(x => x.ContractNumber)
            .MaximumLength(100).When(x => x.ContractNumber is not null).WithMessage("ContractNumber cannot exceed 100 characters.");

        RuleFor(x => x.MinimumContractDate)
            .MaximumLength(50).When(x => x.MinimumContractDate is not null).WithMessage("MinimumContractDate cannot exceed 50 characters.");
    }
}

public class ContractUpdateValidator : AbstractValidator<ContractUpdateDTO>
{
    public ContractUpdateValidator()
    {
        RuleFor(x => x.NoticePeriodUnit).IsInEnum().When(x => x.NoticePeriodUnit.HasValue);
        RuleFor(x => x.NoticePeriodValue).InclusiveBetween(1, 3650).When(x => x.NoticePeriodValue.HasValue)
            .WithMessage("Notice period must be between 1 and 3650.");
        RuleFor(x => x.RenewalMonths).InclusiveBetween(1, 120).When(x => x.RenewalMonths.HasValue)
            .WithMessage("Renewal period must be between 1 and 120 months.");
        RuleFor(x => x.Type).IsInEnum().When(x => x.Type.HasValue);
        RuleFor(x => x.ClearType).Must((dto, clear) => !clear || dto.Type is null)
            .WithMessage("Do not specify Type together with ClearType.");
        RuleFor(x => x.BillingPeriod).IsInEnum().When(x => x.BillingPeriod.HasValue);
        RuleFor(x => x.PriceEffectiveFrom).Must(v => DateOnly.TryParseExact(v, "yyyy-MM-dd", out _))
            .When(x => x.PriceEffectiveFrom != null).WithMessage("Use a valid yyyy-MM-dd price effective date.");
        RuleFor(x => x.PriceNotes).MaximumLength(2000);
        RuleFor(x => x.Title)
            .MaximumLength(200).When(x => x.Title is not null).WithMessage("Title cannot exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).When(x => x.Description is not null).WithMessage("Description cannot exceed 2000 characters.");

        RuleFor(x => x.Status)
            .IsInEnum().When(x => x.Status.HasValue).WithMessage($"Status must be {nameof(ContractStatus.Draft)}, {nameof(ContractStatus.Active)}, {nameof(ContractStatus.Expired)} or {nameof(ContractStatus.Terminated)}.");

        RuleFor(x => x.StartDate)
            .MaximumLength(50).When(x => x.StartDate is not null).WithMessage("StartDate cannot exceed 50 characters.");

        RuleFor(x => x.EndDate)
            .MaximumLength(50).When(x => x.EndDate is not null).WithMessage("EndDate cannot exceed 50 characters.");

        RuleFor(x => x.Currency)
            .MaximumLength(50).When(x => x.Currency is not null).WithMessage("Currency cannot exceed 50 characters.");

        RuleFor(x => x.Value)
            .MaximumLength(500).When(x => x.Value is not null).WithMessage("Value cannot exceed 500 characters.");

        RuleFor(x => x.ContractNumber)
            .MaximumLength(100).When(x => x.ContractNumber is not null).WithMessage("ContractNumber cannot exceed 100 characters.");

        RuleFor(x => x.MinimumContractDate)
            .MaximumLength(50).When(x => x.MinimumContractDate is not null).WithMessage("MinimumContractDate cannot exceed 50 characters.");
    }
}

public class DocumentCreateValidator : AbstractValidator<DocumentCreateDTO>
{
    public DocumentCreateValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("FileName is required.");

        RuleFor(x => x.FileType)
            .NotEmpty().WithMessage("FileType is required.");

        RuleFor(x => x.FileData)
            .NotEmpty().WithMessage("FileData is required.");

        RuleFor(x => x.ContractId)
            .GreaterThan(0).WithMessage("ContractId is required.");
    }
}

public class DocumentMetadataValidator : AbstractValidator<DocumentMetadataDTO>
{
    public DocumentMetadataValidator()
    {
        RuleFor(x => x.Title)
            .MaximumLength(255).When(x => x.Title is not null)
            .WithMessage("Title cannot exceed 255 characters.");
    }
}

public class DocumentReuploadValidator : AbstractValidator<DocumentReuploadDTO>
{
    public DocumentReuploadValidator()
    {
        RuleFor(x => x.FileData)
            .NotEmpty().WithMessage("FileData is required.");

        RuleFor(x => x.FileName)
            .MaximumLength(255).When(x => !string.IsNullOrWhiteSpace(x.FileName))
            .WithMessage("FileName cannot exceed 255 characters.");

        RuleFor(x => x.FileType)
            .MaximumLength(200).When(x => !string.IsNullOrWhiteSpace(x.FileType))
            .WithMessage("FileType cannot exceed 200 characters.");

        RuleFor(x => x.Title)
            .MaximumLength(255).When(x => x.Title is not null)
            .WithMessage("Title cannot exceed 255 characters.");
    }
}

public abstract class AddressFieldsValidator<T> : AbstractValidator<T> where T : AddressCreateDTO
{
    public AddressFieldsValidator()
    {
        RuleFor(x => x.EntityType)
            .IsInEnum().WithMessage("Invalid entity type.");

        RuleFor(x => x.ContractorId)
            .GreaterThanOrEqualTo(1)
            .When(x => x.EntityType == EntityType.Contractor)
            .WithMessage("ContractorId is required for contractor addresses.");

        RuleFor(x => x.ContractId)
            .GreaterThanOrEqualTo(1)
            .When(x => x.EntityType == EntityType.Contract)
            .WithMessage("ContractId is required for contract addresses.");

        RuleFor(x => x.Street)
            .MaximumLength(500).WithMessage("Street cannot exceed 500 characters.");

        RuleFor(x => x.HouseNumber).MaximumLength(100)
            .WithMessage("House number cannot exceed 100 characters.");

        RuleFor(x => x.City)
            .MaximumLength(100).When(x => x.City is not null).WithMessage("City cannot exceed 100 characters.");

        RuleFor(x => x.Zip)
            .MaximumLength(20).When(x => x.Zip is not null).WithMessage("Zip cannot exceed 20 characters.");

        RuleFor(x => x.Country)
            .MaximumLength(100).When(x => x.Country is not null).WithMessage("Country cannot exceed 100 characters.");
    }
}

public class AddressCreateValidator : AddressFieldsValidator<AddressCreateDTO>
{
    public AddressCreateValidator()
    {
        RuleFor(x => x.Street).Must((dto, street) =>
            new[] { street, dto.HouseNumber, dto.City, dto.Zip, dto.Country }
                .Any(value => !string.IsNullOrWhiteSpace(value)))
            .WithMessage("Enter at least one address field.");
    }
}

public class AddressUpdateValidator : AddressFieldsValidator<AddressUpdateDTO> { }
