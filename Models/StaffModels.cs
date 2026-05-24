using System;
using System.Collections.Generic;

namespace OnlineClearanceSystem.Models
{
    // ───────── DASHBOARD ─────────
    public class StaffDashboardViewModel
    {
        public string StaffName     { get; set; } = "";
        public string ActivePeriod  { get; set; } = "—";
        public int    TotalRequests { get; set; }
        public int    TotalStudents { get; set; }
        public int    Approved      { get; set; }
        public int    Pending       { get; set; }
        public int    Declined      { get; set; }
        public List<AnnouncementItem> Announcements { get; set; } = new();
    }

    // ───────── SIGNATORIES LIST ─────────
    public class SignatoryViewModel
    {
        public int    Id          { get; set; }
        public string StudentId   { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string Course      { get; set; } = "";
        public string Status      { get; set; } = "Pending";
        public string Position    { get; set; } = "";  // org position = clearance_key for chat
    }

    // ───────── SIGNED CLEARANCE ─────────
    public class StaffSignedClearance
    {
        public string   StudentId     { get; set; } = "";
        public string   StudentName   { get; set; } = "";
        public string   StudentCourse { get; set; } = "";
        public string   Description   { get; set; } = "";  // Department
        public string   Status        { get; set; } = "";  // "Approved" or "Rejected"
        public DateTime SignedAt      { get; set; }
    }

    // ───────── STAFF PROFILE ─────────
    public class StaffProfileViewModel : ProfileViewModelBase
    {
        public string  StaffId        { get; set; } = "";
        public string  Department     { get; set; } = "";
        public string  Email          { get; set; } = "";
        public List<string> Positions { get; set; } = new();
        public string? SignatureBase64 { get; set; }
        public string? AvatarBase64   { get; set; }
    }
}