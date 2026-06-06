-- ============================================================
-- MIGRATION: Schema consolidation  (MySQL 8+ compatible)
-- Run this ONCE against your MySQL database.
-- ============================================================

SET FOREIGN_KEY_CHECKS = 0;

-- ─────────────────────────────────────────────────────────────
-- HELPERS: stored procedures for conditional DDL
-- ─────────────────────────────────────────────────────────────
DROP PROCEDURE IF EXISTS add_column_if_missing;
DROP PROCEDURE IF EXISTS drop_column_if_exists;
DROP PROCEDURE IF EXISTS drop_fk_if_exists;
DROP PROCEDURE IF EXISTS rename_column_if_exists;
DROP PROCEDURE IF EXISTS rename_announcement_column;
DROP PROCEDURE IF EXISTS drop_index_if_exists;

DELIMITER $$

CREATE PROCEDURE add_column_if_missing(
    IN tbl VARCHAR(64), IN col VARCHAR(64), IN definition TEXT)
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col
    ) THEN
        SET @sql = CONCAT('ALTER TABLE `', tbl, '` ADD COLUMN `', col, '` ', definition);
        PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
    END IF;
END $$

CREATE PROCEDURE drop_column_if_exists(IN tbl VARCHAR(64), IN col VARCHAR(64))
BEGIN
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = col
    ) THEN
        SET @sql = CONCAT('ALTER TABLE `', tbl, '` DROP COLUMN `', col, '`');
        PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
    END IF;
END $$

CREATE PROCEDURE drop_fk_if_exists(IN tbl VARCHAR(64), IN fk VARCHAR(64))
BEGIN
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl
          AND CONSTRAINT_NAME = fk AND CONSTRAINT_TYPE = 'FOREIGN KEY'
    ) THEN
        SET @sql = CONCAT('ALTER TABLE `', tbl, '` DROP FOREIGN KEY `', fk, '`');
        PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
    END IF;
END $$

CREATE PROCEDURE rename_column_if_exists(
    IN tbl VARCHAR(64), IN old_col VARCHAR(64),
    IN new_col VARCHAR(64), IN definition TEXT)
BEGIN
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = old_col
    ) AND NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = new_col
    ) THEN
        SET @sql = CONCAT('ALTER TABLE `', tbl, '` CHANGE COLUMN `', old_col, '` `', new_col, '` ', definition);
        PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
    END IF;
END $$

CREATE PROCEDURE drop_index_if_exists(IN tbl VARCHAR(64), IN idx VARCHAR(64))
BEGIN
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND INDEX_NAME = idx
    ) THEN
        SET @sql = CONCAT('ALTER TABLE `', tbl, '` DROP INDEX `', idx, '`');
        PREPARE s FROM @sql; EXECUTE s; DEALLOCATE PREPARE s;
    END IF;
END $$

DELIMITER ;

-- ─────────────────────────────────────────────────────────────
-- 1. Move student_number + curriculum_id onto users
-- ─────────────────────────────────────────────────────────────
CALL add_column_if_missing('users', 'student_number', 'VARCHAR(50) NULL AFTER id_number');
CALL add_column_if_missing('users', 'curriculum_id',  'INT NULL AFTER student_number');

-- Copy data from students → users (safe if students table still exists)
UPDATE users u
JOIN students s ON s.user_id = u.id
SET u.student_number = s.student_number,
    u.curriculum_id  = s.curriculum_id;

-- ─────────────────────────────────────────────────────────────
-- 2. Create user_signatures (merges signatories + student_signatories)
-- ─────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS user_signatures (
    id             INT          NOT NULL AUTO_INCREMENT PRIMARY KEY,
    user_id        INT          NOT NULL,
    position       VARCHAR(100) NULL,
    signature_data LONGTEXT     NULL,
    UNIQUE KEY uq_user_sig_user (user_id),
    CONSTRAINT fk_user_sig_user FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
);

-- Migrate instructor/staff signatures (position = NULL)
INSERT INTO user_signatures (user_id, position, signature_data)
SELECT user_id, NULL, signature_data FROM signatories
ON DUPLICATE KEY UPDATE signature_data = VALUES(signature_data);

-- Migrate student officer positions
INSERT INTO user_signatures (user_id, position, signature_data)
SELECT user_id, position, signature_data FROM student_signatories
ON DUPLICATE KEY UPDATE
    position       = VALUES(position),
    signature_data = COALESCE(VALUES(signature_data), user_signatures.signature_data);

-- ─────────────────────────────────────────────────────────────
-- 3. Convert clearance_subjects.status  INT → VARCHAR
--    Drop the FK to status_table first, then change the type.
-- ─────────────────────────────────────────────────────────────
CALL drop_fk_if_exists('clearance_subjects', 'clearance_subjects_ibfk_1');

ALTER TABLE clearance_subjects
    MODIFY COLUMN status VARCHAR(20) NOT NULL DEFAULT 'Pending';

UPDATE clearance_subjects SET status = 'Cleared'  WHERE status = '2';
UPDATE clearance_subjects SET status = 'Declined' WHERE status = '3';
UPDATE clearance_subjects SET status = 'Pending'
    WHERE status NOT IN ('Cleared', 'Declined', 'Pending');

-- ─────────────────────────────────────────────────────────────
-- 4. Convert clearance_organization.status  INT → VARCHAR
--    Drop the FK to status_table first, then change the type.
-- ─────────────────────────────────────────────────────────────
CALL drop_fk_if_exists('clearance_organization', 'clearance_organization_ibfk_1');

ALTER TABLE clearance_organization
    MODIFY COLUMN status VARCHAR(20) NOT NULL DEFAULT 'Pending';

UPDATE clearance_organization SET status = 'Cleared'  WHERE status = '2';
UPDATE clearance_organization SET status = 'Declined' WHERE status = '3';
UPDATE clearance_organization SET status = 'Pending'
    WHERE status NOT IN ('Cleared', 'Declined', 'Pending');

-- ─────────────────────────────────────────────────────────────
-- 5. Rename clearance_organization.org_name → position
--    The column is KEPT — only its name changes.
--    Stored values ('Class Adviser', 'SSG President', etc.) stay as-is.
--    MySQL automatically updates the unique index to reference the new name.
-- ─────────────────────────────────────────────────────────────
CALL rename_column_if_exists(
    'clearance_organization', 'org_name', 'position',
    'VARCHAR(200) NOT NULL DEFAULT \'\'');

-- ─────────────────────────────────────────────────────────────
-- 6. Remove org_name from organizations
--    position_title is the sole identifier now.
--    Drop any FK linking clearance_organization.org_name → organizations.org_name first.
-- ─────────────────────────────────────────────────────────────
CALL drop_fk_if_exists('clearance_organization', 'fk_co_org_name');
CALL drop_column_if_exists('organizations', 'org_name');

-- ─────────────────────────────────────────────────────────────
-- 7. Fix announcements column names to match the entity
--    entity uses: body, posted_at, posted_by_id
-- ─────────────────────────────────────────────────────────────
CALL rename_column_if_exists('announcements', 'content',    'body',         'LONGTEXT NOT NULL');
CALL rename_column_if_exists('announcements', 'created_at', 'posted_at',    'DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP');
CALL rename_column_if_exists('announcements', 'author_id',  'posted_by_id', 'INT NULL');

-- ─────────────────────────────────────────────────────────────
-- 8. Fix sections.year_level to INT
-- ─────────────────────────────────────────────────────────────
ALTER TABLE sections
    MODIFY COLUMN year_level INT NOT NULL DEFAULT 1;

-- ─────────────────────────────────────────────────────────────
-- 9. Add FK: users.curriculum_id → curriculum.id
-- ─────────────────────────────────────────────────────────────
CALL drop_fk_if_exists('users', 'fk_users_curriculum');
ALTER TABLE users
    ADD CONSTRAINT fk_users_curriculum
    FOREIGN KEY (curriculum_id) REFERENCES curriculum(id) ON DELETE SET NULL;

-- ─────────────────────────────────────────────────────────────
-- 10. Drop obsolete tables
-- ─────────────────────────────────────────────────────────────
DROP TABLE IF EXISTS students;
DROP TABLE IF EXISTS signatories;
DROP TABLE IF EXISTS student_signatories;
DROP TABLE IF EXISTS status_table;

-- ─────────────────────────────────────────────────────────────
-- 11. Make clearance_organization.uq_co period-aware
--     Old key (student_number, position) blocks the same student from
--     requesting the same position across two different academic periods,
--     causing  ER_DUP_ENTRY ('724-Class Adviser' for key 'uq_co')
--     when a student moves to a new period.
-- ─────────────────────────────────────────────────────────────
CALL add_column_if_missing(
    'clearance_organization', 'period_id',
    'INT NULL AFTER position');

CALL drop_index_if_exists('clearance_organization', 'uq_co');

ALTER TABLE clearance_organization
    ADD UNIQUE KEY uq_co (student_number, position, period_id);

-- ─────────────────────────────────────────────────────────────
-- 12. Subject offerings are global (not bound to an academic period).
--     Make period_id nullable so admin can offer a subject without
--     supplying one — fixes ER_NO_DEFAULT_FOR_FIELD on
--     `period_id` doesn't have a default value
-- ─────────────────────────────────────────────────────────────
ALTER TABLE subject_offerings
    MODIFY COLUMN period_id INT NULL DEFAULT NULL;

-- ─────────────────────────────────────────────────────────────
-- Cleanup helper procedures
-- ─────────────────────────────────────────────────────────────
DROP PROCEDURE IF EXISTS add_column_if_missing;
DROP PROCEDURE IF EXISTS drop_column_if_exists;
DROP PROCEDURE IF EXISTS drop_fk_if_exists;
DROP PROCEDURE IF EXISTS rename_column_if_exists;
DROP PROCEDURE IF EXISTS drop_index_if_exists;

SET FOREIGN_KEY_CHECKS = 1;

SELECT 'Migration complete.' AS result;
