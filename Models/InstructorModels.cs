namespace OnlineClearanceSystem.Models
{
    public class InstructorDashboardViewModel
    {
        public string InstructorName  { get; set; } = "";
        public int    InstructorId    { get; set; } 
        public int EmployeeId { get; set; }
        public string ActivePeriod    { get; set; } = "—";
        public int    SubjectAssigned { get; set; }
        public int    TotalStudents   { get; set; }
        public int    ClearedStudents { get; set; }
        public int    PendingStudents { get; set; }
        public List<AnnouncementItem> Announcements { get; set; } = new();
    }

    public class ClearanceRequest
    {
        public int    Id            { get; set; }
        public string MisCode       { get; set; } = "";
        public string SubjectCode   { get; set; } = "";
        public string Description   { get; set; } = "";
        public string StudentName   { get; set; } = "";
        public string StudentCourse { get; set; } = "";
        public string StudentNumber { get; set; } = "";
    }

    public class OrganizationRequest
    {
        public int    Id          { get; set; }
        public string Position    { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string Course      { get; set; } = "";
        public string Status      { get; set; } = "";
    }

    public class SignedClearance
    {
        public string MisCode       { get; set; } = "";
        public string SubjectCode   { get; set; } = "";
        public string Description   { get; set; } = "";
        public string StudentName   { get; set; } = "";
        public string StudentCourse { get; set; } = "";
        public string Status        { get; set; } = "";
    }

    public class InstructorProfileViewModel : ProfileViewModelBase
    {
        public string EmployeeId   { get; set; } = "";
        public string OrgPosition  { get; set; } = "";
        public string ClassAdviser { get; set; } = "";
        public string Email     { get; set; } = "";

        public List<string> Positions   { get; set; } = new();
        public string? SignatureBase64  { get; set; }
    }

    public class SaveSignatureDto
    {
        public string? SignatureData { get; set; }
    }
}