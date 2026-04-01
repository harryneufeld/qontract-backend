namespace Qontracts.MultiTenant.DTOs
{
    public class TenantDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public TenantType Type { get; set; }
        public string? Settings { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid? ModifiedBy { get; set; }
    }

    public class UserDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid TenantId { get; set; }
    }

    public class ContractorDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public ContractorType Type { get; set; }
        public string? ContactInfo { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
    }

    public class ContractDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ContractStatus Status { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? Value { get; set; }
        public string? Currency { get; set; }
        public Guid? ContractorId { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid ModifiedBy { get; set; }
    }

    public class DocumentDto
    {
        public Guid Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int Version { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid ContractId { get; set; }
        public Guid UserId { get; set; }
    }

    public class AddressDto
    {
        public Guid Id { get; set; }
        public EntityType EntityType { get; set; }
        public Guid EntityId { get; set; }
        public string Street { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? Zip { get; set; }
        public string? Country { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool IsActive { get; set; }
    }

    public class TenantCreateDto
    {
        public string Name { get; set; } = string.Empty;
        public TenantType Type { get; set; }
        public string? Settings { get; set; }
    }

    public class TenantUpdateDto
    {
        public string? Name { get; set; }
        public string? Settings { get; set; }
        public bool? IsActive { get; set; }
    }

    public class UserCreateDto
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
        public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
    }

    public class UserUpdateDto
    {
        public string? Username { get; set; }
        public string? Email { get; set; }
        public UserRole? Role { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ContractorCreateDto
    {
        public string Name { get; set; } = string.Empty;
        public ContractorType Type { get; set; }
        public string? ContactInfo { get; set; }
        public Guid? UserId { get; set; }
    }

    public class ContractorUpdateDto
    {
        public string? Name { get; set; }
        public ContractorType? Type { get; set; }
        public string? ContactInfo { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ContractCreateDto
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public ContractStatus Status { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? Value { get; set; }
        public string? Currency { get; set; }
        public Guid? ContractorId { get; set; }
    }

    public class ContractUpdateDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public ContractStatus? Status { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal? Value { get; set; }
        public string? Currency { get; set; }
        public Guid? ContractorId { get; set; }
    }

    public class DocumentCreateDto
    {
        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
    }

    public class DocumentUpdateDto
    {
        public string? FileName { get; set; }
        public string? FileType { get; set; }
        public Guid? ContractId { get; set; }
        public int? Version { get; set; }
    }
}
