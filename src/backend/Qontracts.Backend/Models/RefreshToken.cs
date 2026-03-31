using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Qontracts.Backend.Models;

namespace Qontracts.Backend.Models
{
    public class RefreshToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public required Guid UserId { get; set; }

        [Required]
        public required string Token { get; set; }

        [MaxLength(500)]
        public required string DeviceInfo { get; set; } = string.Empty;

        [MaxLength(100)]
        public required string IpAddress { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }

        public DateTime RevokedAt { get; set; } = DateTime.MinValue;

        public bool IsRevoked { get; set; } = false;

        public bool IsActive => !IsRevoked && DateTime.UtcNow < ExpiresAt;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public User? User { get; set; }
    }
}