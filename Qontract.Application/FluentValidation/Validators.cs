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
            .EmailAddress().WithMessage("Invalid email format.");

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
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.Email)).WithMessage("Invalid email format.");

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
