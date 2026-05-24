# Online Clearance System — Entity Relationship Diagram

```mermaid
erDiagram

    %% ─────────────────────────────────────────────────────────────
    %% CORE USER TABLES
    %% ─────────────────────────────────────────────────────────────

    users {
        int     id               PK
        varchar first_name
        varchar middle_initial
        varchar last_name
        varchar suffix_name
        varchar email            UK
        varchar password
        varchar id_number        UK
        varchar student_number   UK
        int     curriculum_id    FK
        varchar role
        tinyint is_active
        datetime created_at
    }

    user_signatures {
        int     id            PK
        int     user_id       FK_UK
        varchar position
        text    signature_data
    }

    %% ─────────────────────────────────────────────────────────────
    %% ACADEMIC STRUCTURE
    %% ─────────────────────────────────────────────────────────────

    courses {
        int     id          PK
        varchar course_name
        varchar course_code UK
        tinyint is_active
    }

    curriculum {
        int     id         PK
        int     course_id  FK
        int     year_level
        varchar section
    }

    sections {
        int     id           PK
        int     course_id    FK
        varchar section_name
        int     year_level
        tinyint is_active
    }

    academic_periods {
        int     id         PK
        varchar year_label
        varchar semester
        tinyint is_active
        date    start_date
        date    end_date
    }

    %% ─────────────────────────────────────────────────────────────
    %% SUBJECTS
    %% ─────────────────────────────────────────────────────────────

    subjects {
        int     id           PK
        varchar mis_code     UK
        varchar subject_code
        varchar description
        int     lec_units
        int     lab_units
        tinyint is_active
    }

    subject_offerings {
        int     id         PK
        int     subject_id FK
        int     user_id    FK
        int     period_id  FK
        varchar mis_code   UK
        tinyint is_active
    }

    %% ─────────────────────────────────────────────────────────────
    %% CLEARANCE — SUBJECTS
    %% ─────────────────────────────────────────────────────────────

    clearance_subjects {
        int     id             PK
        varchar student_number
        varchar mis_code
        varchar status
        int     period_id      FK
    }

    %% ─────────────────────────────────────────────────────────────
    %% ORGANIZATIONS & ORG CLEARANCE
    %% ─────────────────────────────────────────────────────────────

    organizations {
        int     id             PK
        varchar position_title
        int     user_id        FK
        int     curriculum_id  FK
        tinyint is_active
    }

    clearance_organization {
        int      id             PK
        varchar  student_number
        varchar  position
        varchar  status
        int      period_id      FK
        datetime created_at
        datetime updated_at
    }

    %% ─────────────────────────────────────────────────────────────
    %% COMMUNICATION & RECORDS
    %% ─────────────────────────────────────────────────────────────

    clearance_messages {
        int      id             PK
        int      sender_id      FK
        varchar  student_number
        varchar  clearance_type
        varchar  clearance_key
        text     message
        datetime sent_at
        tinyint  is_read
    }

    announcements {
        int      id          PK
        int      posted_by_id
        varchar  title
        text     body
        varchar  type
        tinyint  is_pinned
        tinyint  is_active
        datetime posted_at
    }

    signed_clearances {
        int      id             PK
        varchar  student_number
        varchar  student_name
        varchar  course
        varchar  department
        varchar  status
        datetime signed_at
    }

    %% ─────────────────────────────────────────────────────────────
    %% RELATIONSHIPS
    %% ─────────────────────────────────────────────────────────────

    %% User ↔ Curriculum (student belongs to a curriculum)
    users                 }o--o|  curriculum         : "curriculum_id"

    %% User ↔ Signature (one user has one signature row)
    users                 ||--o|  user_signatures    : "user_id"

    %% Curriculum ↔ Course
    curriculum            }o--||  courses            : "course_id"

    %% Sections ↔ Course
    sections              }o--||  courses            : "course_id"

    %% Subject Offerings ↔ Subject / Instructor / Period
    subject_offerings     }o--||  subjects           : "subject_id"
    subject_offerings     }o--||  users              : "user_id (instructor)"
    subject_offerings     }o--||  academic_periods   : "period_id"

    %% Clearance Subjects ↔ Period
    clearance_subjects    }o--||  academic_periods   : "period_id"

    %% Clearance Subjects ↔ Student (via student_number)
    clearance_subjects    }o--o|  users              : "student_number"

    %% Clearance Subjects ↔ Subject Offering (via mis_code)
    clearance_subjects    }o--||  subject_offerings  : "mis_code"

    %% Organizations ↔ Signatory User / Curriculum
    organizations         }o--o|  users              : "user_id (signatory)"
    organizations         }o--o|  curriculum         : "curriculum_id"

    %% Clearance Org ↔ Period / Student
    clearance_organization }o--o| academic_periods   : "period_id"
    clearance_organization }o--o| users              : "student_number"

    %% Clearance Org ↔ Organization (via position title)
    clearance_organization }o--o| organizations      : "position"

    %% Messages ↔ Sender
    clearance_messages    }o--||  users              : "sender_id"

    %% Messages ↔ Student (via student_number)
    clearance_messages    }o--o|  users              : "student_number"

    %% Announcements ↔ Poster
    announcements         }o--o|  users              : "posted_by_id"

    %% Signed Clearances ↔ Student (via student_number)
    signed_clearances     }o--o|  users              : "student_number"
```

---

## Table Summary

| Table | Purpose | Key Columns |
|---|---|---|
| **users** | All system accounts (Student / Instructor / Staff / Admin) | `id`, `email` (UK), `id_number` (UK), `student_number` (UK), `curriculum_id` (FK), `role` |
| **user_signatures** | E-signature storage + student org-officer positions | `user_id` (FK, UNIQUE), `position` (NULL = instructor), `signature_data` |
| **courses** | Course catalog (BSIT, BSCS, etc.) | `id`, `course_code` (UK) |
| **curriculum** | Course + year-level + section combos | `id`, `course_id` (FK), `year_level`, `section` |
| **sections** | Admin-managed section list | `id`, `course_id` (FK), `section_name`, `year_level` |
| **academic_periods** | Semester periods | `id`, `year_label`, `semester`, `is_active` |
| **subjects** | Subject catalog | `id`, `mis_code` (UK), `subject_code`, `lec_units`, `lab_units` |
| **subject_offerings** | Which instructor teaches which subject in which period | `id`, `subject_id` (FK), `user_id` (FK), `period_id` (FK), `mis_code` (UK) |
| **clearance_subjects** | Student subject clearance requests | `student_number`, `mis_code`, `status`, `period_id` (FK) |
| **organizations** | Org position assignments (who signs what) | `id`, `position_title`, `user_id` (FK), `curriculum_id` (FK nullable) |
| **clearance_organization** | Student org clearance requests | `student_number`, `position`, `status`, `period_id` (FK nullable) |
| **clearance_messages** | Private chat between student and signatory | `sender_id` (FK), `student_number`, `clearance_type`, `clearance_key`, `is_read` |
| **announcements** | Admin broadcast messages | `id`, `title`, `body`, `type`, `posted_by_id` |
| **signed_clearances** | Staff-created signed clearance records | `student_number`, `status`, `signed_at` |

---

## Key Design Notes

- **`users.student_number`** is used as a string identifier in `clearance_subjects`, `clearance_organization`, `clearance_messages`, and `signed_clearances` instead of a proper FK — this allows flexible lookups even if the user account doesn't exist yet.
- **`user_signatures`** serves a dual purpose: stores the instructor/staff e-signature (`position = NULL`) AND tracks student org-officer roles (`position = 'SSG President'`).
- **`organizations`** maps a position title to a signatory user. When `curriculum_id IS NULL` it is school-wide (SSG, etc.); when `curriculum_id IS NOT NULL` it is section-specific (Class Adviser).
- **`clearance_organization.position`** links back to `organizations.position_title` logically (no hard FK) so requests survive signatory reassignment.
- **`clearance_messages.clearance_key`** is either a `mis_code` (for subject chats) or a `position_title` (for org chats) — identified by `clearance_type`.
- **`period_id`** was added to `clearance_organization` via migration — older rows backfilled with the then-active period.
