using System;

namespace FOMS.Application.DTOs;

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? RoleId { get; set; }
    public string? Position { get; set; }
    public string? DmsRole { get; set; }
    public string? Phone { get; set; }
    public decimal? SalaryRate { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsActive { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsTemporaryPassword { get; set; }
    public string? TemporaryPassword { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DateHired { get; set; }
}

public class UserCreateDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Position { get; set; }
    public string? DmsRole { get; set; }
    public string? Phone { get; set; }
    public decimal? SalaryRate { get; set; }
    public DateTime? DateHired { get; set; }
}

public class UserUpdateDto
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Position { get; set; }
    public string? DmsRole { get; set; }
    public string? Phone { get; set; }
    public decimal? SalaryRate { get; set; }
    public string Status { get; set; } = "Active";
    public bool IsActive { get; set; } = true;
}
