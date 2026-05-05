using System.ComponentModel.DataAnnotations;

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
}
