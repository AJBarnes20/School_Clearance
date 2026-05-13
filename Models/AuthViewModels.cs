using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace OnlineClearanceSystem.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "ID Number is required.")]
        [Display(Name = "ID Number")]
        public string IdNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }

    public class RegisterViewModel
    {
        // ── Identity (most important) ──────────────────────────
        [Required(ErrorMessage = "First name is required.")]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Display(Name = "Middle Initial")]
        [MaxLength(3)]
        public string? MiddleInitial { get; set; }

        [Required(ErrorMessage = "Last name is required.")]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "ID Number is required.")]
        [Display(Name = "ID Number")]
        public string IdNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email address is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        // ── Student-specific (optional) ────────────────────────
        [Display(Name = "Course")]
        [MaxLength(100)]
        public string? Course { get; set; }

        [Display(Name = "Year Level")]
        [Range(1, 5, ErrorMessage = "Year level must be between 1 and 4.")]
        public int? YearLevel { get; set; }

        [Display(Name = "Section")]
        [MaxLength(50)]
        public string? Section { get; set; }

        public List<SelectListItem> CourseOptions { get; set; } = new();
        public List<SelectListItem> SectionOptions { get; set; } = new();
    }
}