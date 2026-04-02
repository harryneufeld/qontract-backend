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
            .NotEmpty().WithMessage("Tenant type is required.");
    }
}

public class TenantUpdateValidator : AbstractValidator<TenantUpdateDTO>
{
    public TenantUpdateValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Tenant type is required.");

        RuleFor(x => x.Settings)
            .MaximumLength(5000).WithMessage("Settings cannot exceed 5000 characters.");

        RuleFor(x => x.IsActive)
            .Cascade(ValidateMode.Stop).When(x => x.Type.HasValue || !string.IsNullOrEmpty(x.Name));
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
            .EmailAddress().WithMessage("Invalid email format.");

        RuleFor(x => x.PasswordHash)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(255).WithMessage("Password cannot exceed 255 characters.");

        RuleFor(x => x.Role)
            .NotEmpty().WithMessage("Role is required.");
    }
}

public class UserUpdateValidator : AbstractValidator<UserUpdateDTO>
{
    public UserUpdateValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email)).WithMessage("Invalid email format.");

        RuleFor(x => x.PasswordHash)
            .MaximumLength(255).When(x => !string.IsNullOrEmpty(x.PasswordHash)).WithMessage("Password cannot exceed 255 characters.");

        RuleFor(x => x.Role)
            .NotEmpty().When(x => x.Role.HasValue).WithMessage("Role is required when specified.");

        RuleFor(x => x.IsActive)
            .Cascade(ValidateMode.Stop).When(x => x.Name.HasValue || x.Email.HasValue || x.Role.HasValue);
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
            .NotEmpty().WithMessage("Contractor type is required.");

        RuleFor(x => x.ContactInfo)
            .MaximumLength(500).WithMessage("Contact info cannot exceed 500 characters.");
    }
}

public class ContractorUpdateValidator : AbstractValidator<ContractorUpdateDTO>
{
    public ContractorUpdateValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");

        RuleFor(x => x.Type)
            .NotEmpty().When(x => x.Type.HasValue).WithMessage("Contractor type is required when specified.");

        RuleFor(x => x.ContactInfo)
            .MaximumLength(500).WithMessage("Contact info cannot exceed 500 characters.");

        RuleFor(x => x.IsActive)
            .Cascade(ValidateMode.Stop).When(x => x.Name.HasValue || x.Type.HasValue || x.ContactInfo.HasValue);
    }
}

public class ContractCreateValidator : AbstractValidator<ContractCreateDTO>
{
    public ContractCreateValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.");

        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("Status is required.");

        RuleFor(x => x.StartDate)
            .MaximumLength(50).WithMessage("Start date cannot exceed 50 characters.");

        RuleFor(x => x.EndDate)
            .MaximumLength(50).WithMessage("End date cannot exceed 50 characters.");

        RuleFor(x => x.Counterparty)
            .MaximumLength(100).WithMessage("Counterparty cannot exceed 100 characters.");

        RuleFor(x => x.Currency)
            .MaximumLength(50).WithMessage("Currency cannot exceed 50 characters.");

        RuleFor(x => x.Value)
            .MaximumLength(500).WithMessage("Value cannot exceed 500 characters.");

        RuleFor(x => x.ContractorId)
            .MaximumLength(500).WithMessage("Contractor ID cannot exceed 500 characters.");
    }
}

public class ContractUpdateValidator : AbstractValidator<ContractUpdateDTO>
{
    public ContractUpdateValidator()
    {
        RuleFor(x => x.Title)
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.");

        RuleFor(x => x.Status)
            .NotEmpty().When(x => x.Status.HasValue).WithMessage("Status is required when specified.");

        RuleFor(x => x.StartDate)
            .MaximumLength(50).WithMessage("Start date cannot exceed 50 characters.");

        RuleFor(x => x.EndDate)
            .MaximumLength(50).WithMessage("End date cannot exceed 50 characters.");

        RuleFor(x => x.Counterparty)
            .MaximumLength(100).WithMessage("Counterparty cannot exceed 100 characters.");

        RuleFor(x => x.Currency)
            .MaximumLength(50).WithMessage("Currency cannot exceed 50 characters.");

        RuleFor(x => x.Value)
            .MaximumLength(500).WithMessage("Value cannot exceed 500 characters.");

        RuleFor(x => x.ContractorId)
            .MaximumLength(500).WithMessage("Contractor ID cannot exceed 500 characters.");

        RuleFor(x => x.Title)
            .Cascade(ValidateMode.Stop).When(x => !string.IsNullOrEmpty(x.Title));

        RuleFor(x => x.Description)
            .Cascade(ValidateMode.Stop).When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Status)
            .Cascade(ValidateMode.Stop).When(x => x.Status.HasValue);

        RuleFor(x => x.StartDate)
            .Cascade(ValidateMode.Stop).When(x => !string.IsNullOrEmpty(x.StartDate));

        RuleFor(x => x.EndDate)
            .Cascade(ValidateMode.Stop).When(x => !string.IsNullOrEmpty(x.EndDate));

        RuleFor(x => x.Counterparty)
            .Cascade(ValidateMode.Stop).When(x => !string.IsNullOrEmpty(x.Counterparty));

        RuleFor(x => x.Currency)
            .Cascade(ValidateMode.Stop).When(x => !string.IsNullOrEmpty(x.Currency));

        RuleFor(x => x.Value)
            .Cascade(ValidateMode.Stop).When(x => !string.IsNullOrEmpty(x.Value));

        RuleFor(x => x.ContractorId)
            .Cascade(ValidateMode.Stop).When(x => x.ContractorId.HasValue);
    }
}

public class DocumentCreateValidator : AbstractValidator<DocumentCreateDTO>
{
    public DocumentCreateValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("File name is required.")
            .MaximumLength(255).WithMessage("File name cannot exceed 255 characters.");

        RuleFor(x => x.FileType)
            .NotEmpty().WithMessage("File type is required.")
            .MaximumLength(200).WithMessage("File type cannot exceed 200 characters.");

        RuleFor(x => x.FileSize)
            .GreaterThan(0).WithMessage("File size must be greater than 0.");
    }
}

public class AddressCreateValidator : AbstractValidator<AddressCreateDTO>
{
    public AddressCreateValidator()
    {
        RuleFor(x => x.EntityType)
            .NotEmpty().WithMessage("Entity type is required.");

        RuleFor(x => x.Street)
            .NotEmpty().WithMessage("Street is required.")
            .MaximumLength(500).WithMessage("Street cannot exceed 500 characters.");

        RuleFor(x => x.City)
            .MaximumLength(100).WithMessage("City cannot exceed 100 characters.");

        RuleFor(x => x.Zip)
            .MaximumLength(20).WithMessage("Zip cannot exceed 20 characters.");

        RuleFor(x => x.Country)
            .MaximumLength(100).WithMessage("Country cannot exceed 100 characters.");
    }
}
