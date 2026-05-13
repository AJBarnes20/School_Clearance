namespace OnlineClearanceSystem.Models
{
    // ── Dashboard ─────────────────────────────────────────────────────────────
    public class StudentDashboardViewModel
    {
        public required string StudentName       { get; set; }
        public int             SubjectCleared    { get; set; }
        public int             SubjectIncomplete { get; set; }
        public int             OrgCleared        { get; set; }
        public int             TotalSubjects     { get; set; }
        public int             TotalOrgs         { get; set; }
        public string          ActivePeriod      { get; set; } = "A.Y. 2025-2026, 2nd Semester";
        public List<AnnouncementItem> Announcements { get; set; } = new();
    }

    // ── Profile ───────────────────────────────────────────────────────────────
    public class StudentProfileViewModel : ProfileViewModelBase
    {
        public string StudentId { get; set; } = "";
        public string Course    { get; set; } = "";
        public string YearLevel { get; set; } = "";
        public string Section   { get; set; } = "";
        public string Email     { get; set; } = "";

        public List<string>                AvailableCourses  { get; set; } = new();
        public List<SectionItem>           AvailableSections { get; set; } = new();
        public List<OrganizationSignatory> Positions         { get; set; } = new();

        public string? SignaturePath { get; set; }
    }

    public class SectionItem
    {
        public string SectionName { get; set; } = "";
        public int    YearLevel   { get; set; }
        public string CourseCode  { get; set; } = "";
    }

    // ── Clearance (subject rows) ──────────────────────────────────────────────
    public class StudentClearanceItem
    {
        public required string MisCode        { get; set; }
        public required string SubjectCode    { get; set; }
        public required string Description    { get; set; }
        public required string InstructorName { get; set; }
        public required string Status         { get; set; }
    }

    // ── Subjects Offered ──────────────────────────────────────────────────────
    public class SubjectOfferedViewModel
    {
        public List<SubjectItem> AvailableSubjects { get; set; } = new();
        public string            ActivePeriod      { get; set; } = "A.Y. 2025-2026, 2nd Semester";
        public int               ActivePeriodId    { get; set; } = 0;
    }

    public class SubjectItem
    {
        public string Id              { get; set; } = "";
        public string MisCode         { get; set; } = "";
        public string SubjectCode     { get; set; } = "";
        public string Description     { get; set; } = "";
        public string InstructorName  { get; set; } = "";
        public bool   AlreadyEnrolled { get; set; } = false;
        public string EnrolledStatus  { get; set; } = "";
    }

    // ── PDF Download ──────────────────────────────────────────────────────────
    public class StudentClearancePdfViewModel
    {
        public string StudentName    { get; set; } = "";
        public string StudentId      { get; set; } = "";
        public string CourseYear     { get; set; } = "";
        public string AySemester     { get; set; } = "";
        public int    ActivePeriodId { get; set; } = 0;
        public List<PdfSubjectItem>      Subjects      { get; set; } = new();
        public List<PdfOrganizationItem> Organizations { get; set; } = new();
    }

    public class PdfSubjectItem
    {
        public string MisCode         { get; set; } = "";
        public string SubjectCode     { get; set; } = "";
        public string Description     { get; set; } = "";
        public string InstructorName  { get; set; } = "";
        public string Status          { get; set; } = "";
        public string SignatureBase64  { get; set; } = "";
    }

    public class PdfOrganizationItem
    {
        public string OrgName         { get; set; } = "";
        public string Role            { get; set; } = "";
        public string PersonName      { get; set; } = "";
        public string Status          { get; set; } = "";
        public string SignatureBase64  { get; set; } = "";
        public bool   IsSelfSignatory  { get; set; } = false;
    }
}