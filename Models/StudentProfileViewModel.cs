namespace OnlineClearanceSystem.Models
{
    public class StudentProfileViewModel
    {
        public string StudentId     { get; set; } = "";
        public string FirstName     { get; set; } = "";
        public string MiddleInitial { get; set; } = "";
        public string LastName      { get; set; } = "";
        public string Suffix        { get; set; } = "";
        public string Course        { get; set; } = "BSIT";
        public string YearLevel     { get; set; } = "2nd Year";
        public string Section       { get; set; } = "A";
        public string Username      { get; set; } = "";
        public string Password      { get; set; } = "";

        // Computed
        public string FullName =>
            $"{FirstName} {(string.IsNullOrEmpty(MiddleInitial) ? "" : MiddleInitial + ". ")}{LastName} {Suffix}".Trim();

        // Signature: relative web path stored in signatories.uploaded_signature_path
        // Null = no signature on file yet → view shows "Not yet saved"
        public string? SignaturePath { get; set; }

        // Positions from organizations table matched by curriculum_id.
        // Empty list = student has no assigned org position →
        // the entire E-Signature + Assigned Organization section is hidden in the view.
        public List<OrganizationSignatory> Positions { get; set; } = new();
    }
}
