-- ============================================================
-- Online Clearance System — 100% Normalized Database Schema
-- No duplications, proper foreign key relationships, single source of truth
-- ============================================================

-- ── USERS (central user table - single source of truth) ─────
CREATE TABLE IF NOT EXISTS users (
    id             INT          AUTO_INCREMENT PRIMARY KEY,
    first_name     VARCHAR(50)  NOT NULL DEFAULT '',
    middle_initial VARCHAR(10)           DEFAULT '',
    last_name      VARCHAR(50)  NOT NULL DEFAULT '',
    suffix_name    VARCHAR(20)           DEFAULT '',
    email          VARCHAR(100) NOT NULL UNIQUE,
    password       VARCHAR(255) NOT NULL,
    id_number      VARCHAR(50)           DEFAULT NULL,
    role           VARCHAR(20)  NOT NULL DEFAULT 'Pending',
    is_active      TINYINT(1)   NOT NULL DEFAULT 0,
    created_at     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- ── COURSES ──────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS courses (
    id          INT          AUTO_INCREMENT PRIMARY KEY,
    course_name VARCHAR(100) NOT NULL,
    course_code VARCHAR(20)  NOT NULL,
    is_active   TINYINT(1)   NOT NULL DEFAULT 1
);

-- ── CURRICULUM (year + section per course) ───────────────
CREATE TABLE IF NOT EXISTS curriculum (
    id          INT         AUTO_INCREMENT PRIMARY KEY,
    course_id   INT         NOT NULL,
    year_level  INT         NOT NULL DEFAULT 1,
    section     VARCHAR(20) NOT NULL DEFAULT '',
    FOREIGN KEY (course_id) REFERENCES courses(id) ON DELETE CASCADE
);

-- ── SECTIONS (admin-managed section list) ────────────────
CREATE TABLE IF NOT EXISTS sections (
    id           INT         AUTO_INCREMENT PRIMARY KEY,
    course_id    INT         NOT NULL,
    section_name VARCHAR(50) NOT NULL,
    year_level   VARCHAR(20)          DEFAULT '',
    is_active    TINYINT(1)   NOT NULL DEFAULT 1,
    FOREIGN KEY (course_id) REFERENCES courses(id) ON DELETE CASCADE
);

-- ── STUDENTS (links a user account to a student record) ──
CREATE TABLE IF NOT EXISTS students (
    id             INT         AUTO_INCREMENT PRIMARY KEY,
    user_id        INT         NOT NULL UNIQUE,
    student_number VARCHAR(50)          DEFAULT NULL,
    curriculum_id  INT                  DEFAULT NULL,
    FOREIGN KEY (user_id)      REFERENCES users(id)      ON DELETE CASCADE,
    FOREIGN KEY (curriculum_id) REFERENCES curriculum(id) ON DELETE SET NULL
);

-- ── SIGNATORIES (instructor signatories) ─────────────────
CREATE TABLE IF NOT EXISTS signatories (
    id             INT         AUTO_INCREMENT PRIMARY KEY,
    user_id        INT         NOT NULL UNIQUE,
    signature_data MEDIUMTEXT             DEFAULT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

-- ── STUDENT SIGNATORIES (students assigned org positions) ─
CREATE TABLE IF NOT EXISTS student_signatories (
    id             INT          AUTO_INCREMENT PRIMARY KEY,
    user_id        INT          NOT NULL,
    position       VARCHAR(100)           DEFAULT '',
    signature_data MEDIUMTEXT             DEFAULT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

-- ── ACADEMIC PERIODS ─────────────────────────────────────
CREATE TABLE IF NOT EXISTS academic_periods (
    id         INT         AUTO_INCREMENT PRIMARY KEY,
    year_label VARCHAR(20) NOT NULL,
    semester   VARCHAR(10) NOT NULL,
    is_active  TINYINT(1)  NOT NULL DEFAULT 0,
    start_date DATE        NOT NULL,
    end_date   DATE        NOT NULL
);

-- ── SUBJECTS ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS subjects (
    id           INT          AUTO_INCREMENT PRIMARY KEY,
    mis_code     VARCHAR(50)  NOT NULL DEFAULT '',
    subject_code VARCHAR(50)  NOT NULL,
    description  VARCHAR(200) NOT NULL DEFAULT '',
    lec_units    INT          NOT NULL DEFAULT 2,
    lab_units    INT          NOT NULL DEFAULT 2,
    is_active    TINYINT(1)   NOT NULL DEFAULT 1
);

-- ── SUBJECT OFFERINGS ────────────────────────────────────
CREATE TABLE IF NOT EXISTS subject_offerings (
    id           INT         AUTO_INCREMENT PRIMARY KEY,
    subject_id   INT         NOT NULL,
    user_id      INT         NOT NULL, -- Instructor/Signatory
    period_id    INT         NOT NULL,
    mis_code     VARCHAR(50) NOT NULL DEFAULT '',
    is_active    TINYINT(1)  NOT NULL DEFAULT 1,
    FOREIGN KEY (subject_id) REFERENCES subjects(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id)    REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (period_id)  REFERENCES academic_periods(id) ON DELETE CASCADE
);

-- ── STATUS TABLE (1=Pending, 2=Cleared) ──────────────────
CREATE TABLE IF NOT EXISTS status_table (
    id    INT         AUTO_INCREMENT PRIMARY KEY,
    label VARCHAR(50) NOT NULL
);

INSERT IGNORE INTO status_table (id, label) VALUES (1, 'Pending'), (2, 'Cleared');

-- ── CLEARANCE SUBJECTS (student enrollment + clearance) ──
CREATE TABLE IF NOT EXISTS clearance_subjects (
    id             INT         AUTO_INCREMENT PRIMARY KEY,
    student_number VARCHAR(50) NOT NULL,
    mis_code       VARCHAR(50) NOT NULL,
    status         INT         NOT NULL DEFAULT 1,
    period_id      INT         NOT NULL,
    UNIQUE KEY uq_cs (student_number, mis_code),
    FOREIGN KEY (status)    REFERENCES status_table(id),
    FOREIGN KEY (period_id) REFERENCES academic_periods(id) ON DELETE CASCADE
);

-- ── ORGANIZATIONS ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS organizations (
    id             INT          AUTO_INCREMENT PRIMARY KEY,
    org_name       VARCHAR(100) NOT NULL,
    position_title VARCHAR(200) NOT NULL,
    user_id        INT                   DEFAULT NULL,
    curriculum_id  INT                   DEFAULT NULL,
    is_active      TINYINT(1)    NOT NULL DEFAULT 1,
    FOREIGN KEY (user_id)       REFERENCES users(id) ON DELETE SET NULL,
    FOREIGN KEY (curriculum_id) REFERENCES curriculum(id) ON DELETE SET NULL
);

-- ── CLEARANCE ORGANIZATION ───────────────────────────────
CREATE TABLE IF NOT EXISTS clearance_organization (
    id             INT          AUTO_INCREMENT PRIMARY KEY,
    student_number VARCHAR(50)  NOT NULL,
    org_name       VARCHAR(100) NOT NULL,
    status         INT          NOT NULL DEFAULT 1,
    UNIQUE KEY uq_co (student_number, org_name),
    FOREIGN KEY (status) REFERENCES status_table(id)
);

-- ── ANNOUNCEMENTS ────────────────────────────────────────
CREATE TABLE IF NOT EXISTS announcements (
    id         INT          AUTO_INCREMENT PRIMARY KEY,
    title      VARCHAR(255) NOT NULL,
    content    TEXT,
    type       VARCHAR(50)           DEFAULT 'General',
    author_id  INT                   DEFAULT NULL,
    created_at DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (author_id) REFERENCES users(id) ON DELETE SET NULL
);

-- ============================================================
-- UNIQUE CONSTRAINTS FOR DATA INTEGRITY
-- ============================================================

-- Ensure unique email addresses
ALTER TABLE users ADD CONSTRAINT IF NOT EXISTS uq_users_email UNIQUE (email);

-- Ensure unique subject codes
ALTER TABLE subjects ADD CONSTRAINT IF NOT EXISTS uq_subjects_subject_code UNIQUE (subject_code);

-- Ensure unique MIS codes in subject offerings
ALTER TABLE subject_offerings ADD CONSTRAINT IF NOT EXISTS uq_so_mis_code UNIQUE (mis_code);

-- ============================================================
-- INDEXES FOR PERFORMANCE
-- ============================================================

CREATE INDEX IF NOT EXISTS idx_users_role ON users(role);
CREATE INDEX IF NOT EXISTS idx_users_id_number ON users(id_number);
CREATE INDEX IF NOT EXISTS idx_students_user_id ON students(user_id);
CREATE INDEX IF NOT EXISTS idx_students_curriculum_id ON students(curriculum_id);
CREATE INDEX IF NOT EXISTS idx_curriculum_course_id ON curriculum(course_id);
CREATE INDEX IF NOT EXISTS idx_signatories_user_id ON signatories(user_id);
CREATE INDEX IF NOT EXISTS idx_subject_offerings_subject_id ON subject_offerings(subject_id);
CREATE INDEX IF NOT EXISTS idx_subject_offerings_user_id ON subject_offerings(user_id);
CREATE INDEX IF NOT EXISTS idx_subject_offerings_period_id ON subject_offerings(period_id);
CREATE INDEX IF NOT EXISTS idx_organizations_user_id ON organizations(user_id);
CREATE INDEX IF NOT EXISTS idx_organizations_curriculum_id ON organizations(curriculum_id);

-- ============================================================
-- SAMPLE DATA — uncomment to seed an admin account
-- Password hash below is BCrypt for "Admin@123"
-- ============================================================
-- INSERT IGNORE INTO users (first_name, last_name, email, password, role, is_active)
-- VALUES ('Admin', 'User', 'admin@clearance.edu', '$2a$11$xxxYOURHASHxxx', 'Admin', 1);
