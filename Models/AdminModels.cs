using System.Collections.Generic;

namespace OnlineClearanceSystem.Models
{
    public class AdminDashboardViewModel
    {
        public string AdminName        { get; set; } = "";
        public string ActivePeriod     { get; set; } = "—";
        public int    TotalStudents    { get; set; }
        public int    TotalInstructors { get; set; }
        public int    TotalStaff       { get; set; }
        public int    PendingUsers     { get; set; }
        public int    TotalCleared     { get; set; }
        public int    TotalPending     { get; set; }
        public List<AnnouncementItem> Announcements { get; set; } = new();
    }

    public class UserManagementItem
    {
        public int    Id        { get; set; }
        public string Username  { get; set; } = "";
        public string FullName  { get; set; } = "";
        public string IdNumber  { get; set; } = "";
        public string Role      { get; set; } = "";
        public bool   IsActive  { get; set; }
        public string CreatedAt { get; set; } = "";
    }

    public class AdminAnnouncementViewModel
    {
        public int    Id      { get; set; }
        public string Title   { get; set; } = "";
        public string Content { get; set; } = "";
        public string Type    { get; set; } = "General";
    }

    public class AcademicPeriodItem
    {
        public int    Id           { get; set; }
        public string AcademicYear { get; set; } = "";
        public string Semester     { get; set; } = "";
        public bool   IsActive     { get; set; }
    }

    public class AdminSubjectItem
    {
        public int    Id          { get; set; }
        public string SubjectCode { get; set; } = "";
        public string Title       { get; set; } = "";
        public int    LecUnits    { get; set; }
        public int    LabUnits    { get; set; }
    }

    public class AdminSubjectOfferingItem
    {
        public int    Id             { get; set; }
        public string MisCode        { get; set; } = "";
        public string SubjectCode    { get; set; } = "";
        public string Description    { get; set; } = "";
        public string InstructorName { get; set; } = "";
        public string Period         { get; set; } = "";
    }

    public class AdminStaffItem
    {
        public int    Id         { get; set; }
        public string Name       { get; set; } = "";
        public string Username   { get; set; } = "";
        public string EmployeeId { get; set; } = "—";
        public string Position   { get; set; } = "—";
        public int    Approved   { get; set; }
        public int    Pending    { get; set; }
    }

    public class AdminInstructorItem
    {
        public int    Id         { get; set; }
        public string Name       { get; set; } = "";
        public string Username   { get; set; } = "";
        public string EmployeeId { get; set; } = "—";
        public int    Subjects   { get; set; }
    }

    public class AdminStudentItem
    {
        public int    Id        { get; set; }
        public string Name      { get; set; } = "";
        public string IdNum     { get; set; } = "—";
        public string Course    { get; set; } = "—";
        public int    YearLevel { get; set; }
        public string Section   { get; set; } = "—";
        public string Status    { get; set; } = "Incomplete";
    }
}