namespace Qontracts.MultiTenant.Domain
{
    using System;
    using System.ComponentModel.DataAnnotations;

    public class Tenant
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        public TenantType Type { get; set; }

        public string? Settings { get; set; } // JSON configuration

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CreatedBy { get; set; } // Foreign key to User

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public Guid? ModifiedBy { get; set; } // Foreign key to User

        // Navigation properties
        public virtual ICollection<User> Users { get; set; } = new List<User>();
        public virtual ICollection<Contractor> Contractors { get; set; } = new List<Contractor>();
        public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();
        public virtual ICollection<Address> Addresses { get; set; } = new List<Address>();
    }

    public class User
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(50)]
        public required string Username { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(100)]
        public required string Email { get; set; }

        [Required]
        public required byte[] PasswordHash { get; set; }

        [Required]
        public required byte[] PasswordSalt { get; set; }

        public UserRole Role { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Foreign keys
        public Guid TenantId { get; set; }

        // Navigation properties
        public virtual Tenant? Tenant { get; set; }
        public virtual ICollection<Address> Addresses { get; set; } = new List<Address>();
    }

    public class Contractor
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        public ContractorType Type { get; set; }

        public string? ContactInfo { get; set; } // JSON with email, phone, etc.

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Foreign keys
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; } // For internal tracking if contractor is also a user

        // Navigation properties
        public virtual Tenant? Tenant { get; set; }
        public virtual User? User { get; set; }
        public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();
    }

    public class Contract
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(200)]
        public required string Title { get; set; }

        public string? Description { get; set; }

        public ContractStatus Status { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public decimal? Value { get; set; }
        public string? Currency { get; set; }

        public Guid? ContractorId { get; set; }

        public Guid TenantId { get; set; }

        public Guid CreatedBy { get; set; }
        public Guid ModifiedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual Tenant? Tenant { get; set; }
        public virtual Contractor? Contractor { get; set; }
        public virtual User? CreatedByUser { get; set; }
        public virtual User? ModifiedByUser { get; set; }
        public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
    }

    public class Document
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public string FileName { get; set; } = string.Empty;

        public string FileType { get; set; } = string.Empty; // MIME type

        public string FilePath { get; set; } = string.Empty;

        public long fileSize { get; set; }

        public Guid ContractId { get; set; }
        public Guid UserId { get; set; }
        public Guid CreatedBy { get; set; }
        public Guid ModifiedBy { get; set; }
        public int Version { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual Contract Contract { get; set; }
        public virtual User UploadedBy { get; set; }
        public virtual User CreatedByUser { get; set; }
        public virtual User ModifiedByUser { get; set; }
    }
}
