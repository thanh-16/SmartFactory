namespace SmartFactory.Api.Models.Entities;

public class AppUser
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // Supervisor, KCS, Manager
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
