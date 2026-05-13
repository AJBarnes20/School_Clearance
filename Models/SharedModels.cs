namespace OnlineClearanceSystem.Models
{
    public class AnnouncementItem
    {
        public string Title   { get; set; } = "";
        public string Content { get; set; } = "";
        public string Type    { get; set; } = "General"; // Pinned, Urgent, Event, General
        public string Date    { get; set; } = "";
    }

    public abstract class ProfileViewModelBase
    {
        public string FirstName     { get; set; } = "";
        public string MiddleInitial { get; set; } = "";
        public string LastName      { get; set; } = "";
        public string Suffix        { get; set; } = "";
        public string Password      { get; set; } = "";

        public string FullName =>
            $"{FirstName} {(string.IsNullOrEmpty(MiddleInitial) ? "" : MiddleInitial + ". ")}{LastName} {Suffix}".Trim();
    }

    public class OrganizationSignatory
    {
        public string OrgName         { get; set; } = "";
        public string OrgRole         { get; set; } = "";
        public string PersonName      { get; set; } = "";
        public string Status          { get; set; } = "";
        public bool   IsSelfSignatory { get; set; } = false;
    }

    public class StudentClearanceViewModel
    {
        public List<StudentClearanceItem>  SubjectItems { get; set; } = new();
        public OrganizationSignatory?      ClassAdviser { get; set; }
        public List<OrganizationSignatory> OrgItems     { get; set; } = new();
    }

    public class RequestOrgDto
    {
        public string? OrgName   { get; set; }
        public int     PeriodId  { get; set; }  // 0 = use active period
    }

    public class DeleteClearanceDto
    {
        public string? Type     { get; set; }  // "subject", "org", "adviser"
        public string? Key      { get; set; }  // mis_code or position name
        public int     PeriodId { get; set; }  // 0 = active period
    }

    public class SendClearanceMessageDto
    {
        public string? ClearanceType { get; set; }  // "subject" or "org"
        public string? ClearanceKey  { get; set; }  // mis_code or position name
        public string? Message       { get; set; }
    }

    public class SubjectOfferingDto
    {
        public string MisCode     { get; set; } = "";
        public string SubjectCode { get; set; } = "";
        public string Description { get; set; } = "";
        public int    LabUnit     { get; set; }
        public int    LecUnit     { get; set; }
    }
}