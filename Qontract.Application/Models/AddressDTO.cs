using Qontract.Domain.Models;
using System.ComponentModel.DataAnnotations;

namespace Qontract.Application.Models;

public class AddressDTO
{
    public int Id { get; set; }
    public EntityType EntityType { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
}

public class AddressCreateDTO
{
    [Required]
    [StringLength(50)]
    public EntityType EntityType { get; set; }

    [Required]
    [MaxLength(500)]
    public string Street { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(20)]
    public string? Zip { get; set; }

    [MaxLength(100)]
    public string? Country { get; set; }
}
