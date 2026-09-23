using FluentValidation;
using Qontract.Application.Models;
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

public class UserCreateValidator : AbstractValidator<UserCreateDTO>
{
    public UserCreateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email format.")
            .MaximumLength(100).WithMessage("Email cannot exceed 100 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(255).WithMessage("Password cannot exceed 255 characters.");

        RuleFor(x => x.Role)
            .IsInEnum().WithMessage("Invalid role.");
    }
}

public class UserUpdateValidator : AbstractValidator<UserUpdateDTO>
{
    public UserUpdateValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email)).WithMessage("Invalid email format.")
            .MaximumLength(100).When(x => !string.IsNullOrEmpty(x.Email)).WithMessage("Email cannot exceed 100 characters.");

        RuleFor(x => x.Password)
            .MaximumLength(255).When(x => !string.IsNullOrEmpty(x.Password)).WithMessage("Password cannot exceed 255 characters.");

        RuleFor(x => x.Role)
            .IsInEnum().When(x => x.Role.HasValue).WithMessage("Invalid role.");

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

        RuleFor(x => x.ContactInfo)
            .MaximumLength(500).When(x => x.ContactInfo is not null).WithMessage("ContactInfo cannot exceed 500 characters.");
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

        RuleFor(x => x.ContactInfo)
            .MaximumLength(500).When(x => x.ContactInfo is not null).WithMessage("ContactInfo cannot exceed 500 characters.");
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

public class AddressCreateValidator : AbstractValidator<AddressCreateDTO>
{
    public AddressCreateValidator()
    {
        RuleFor(x => x.EntityType)
            .IsInEnum().WithMessage("Invalid entity type.");

        RuleFor(x => x.Street)
            .NotEmpty().WithMessage("Street is required.")
            .MaximumLength(500).WithMessage("Street cannot exceed 500 characters.");

        RuleFor(x => x.City)
            .MaximumLength(100).When(x => x.City is not null).WithMessage("City cannot exceed 100 characters.");

        RuleFor(x => x.Zip)
            .MaximumLength(20).When(x => x.Zip is not null).WithMessage("Zip cannot exceed 20 characters.");

        RuleFor(x => x.Country)
            .MaximumLength(100).When(x => x.Country is not null).WithMessage("Country cannot exceed 100 characters.");
    }
}
