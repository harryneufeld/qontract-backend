namespace Qontracts.MultiTenant.Domain
{
    using System;
    using System.ComponentModel.DataAnnotations;

    public class Address
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public EntityType EntityType { get; set; }
        public Guid EntityId { get; set; }

        [Required]
        [MaxLength(200)]
        public required string Street { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        [MaxLength(20)]
        public string? Zip { get; set; }

        [MaxLength(100)]
        public string? Country { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;
    }
}
