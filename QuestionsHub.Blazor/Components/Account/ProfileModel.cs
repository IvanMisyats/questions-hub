using System.ComponentModel.DataAnnotations;

namespace QuestionsHub.Blazor.Components.Account;

public class ProfileModel
{
    [Required(ErrorMessage = "Ім'я обов'язкове")]
    [StringLength(50, ErrorMessage = "Ім'я не може бути довшим за 50 символів")]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Прізвище обов'язкове")]
    [StringLength(50, ErrorMessage = "Прізвище не може бути довшим за 50 символів")]
    public string LastName { get; set; } = "";

    [StringLength(100, ErrorMessage = "Місто не може бути довшим за 100 символів")]
    public string? City { get; set; }

    [StringLength(100, ErrorMessage = "Команда не може бути довшою за 100 символів")]
    public string? Team { get; set; }

    public string Email { get; set; } = "";
}

