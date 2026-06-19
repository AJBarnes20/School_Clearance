-- MySQL dump 10.13  Distrib 8.0.46, for Win64 (x86_64)
--
-- Host: localhost    Database: onlineclearance
-- ------------------------------------------------------
-- Server version	9.6.0

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!50503 SET NAMES utf8 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;
SET @MYSQLDUMP_TEMP_LOG_BIN = @@SESSION.SQL_LOG_BIN;
SET @@SESSION.SQL_LOG_BIN= 0;

--
-- GTID state at the beginning of the backup 
--

SET @@GLOBAL.GTID_PURGED=/*!80000 '+'*/ 'f41c07ec-4b58-11f1-8d0a-d45d646df2e8:1-845';

--
-- Table structure for table `clearance_subjects`
--

DROP TABLE IF EXISTS `clearance_subjects`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `clearance_subjects` (
  `id` int NOT NULL AUTO_INCREMENT,
  `student_number` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL,
  `mis_code` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL,
  `status` varchar(20) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'Pending',
  `period_id` int NOT NULL,
  `requested_at` datetime DEFAULT NULL,
  `signed_at` datetime DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_cs` (`student_number`,`mis_code`,`period_id`),
  KEY `idx_clearance_subjects_student` (`student_number`),
  KEY `idx_clearance_subjects_mis_code` (`mis_code`),
  KEY `idx_clearance_subjects_period` (`period_id`),
  KEY `status` (`status`),
  CONSTRAINT `clearance_subjects_ibfk_2` FOREIGN KEY (`period_id`) REFERENCES `academic_periods` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB AUTO_INCREMENT=36 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `clearance_subjects`
--

LOCK TABLES `clearance_subjects` WRITE;
/*!40000 ALTER TABLE `clearance_subjects` DISABLE KEYS */;
INSERT INTO `clearance_subjects` VALUES (8,'724','GT-4','Cleared',3,'2026-06-03 16:05:48',NULL),(18,'724','GT-61','Cleared',3,'2026-06-03 16:05:48',NULL),(19,'724','GT-53','Cleared',3,'2026-06-03 16:05:48',NULL),(23,'724','GT-56','Cleared',3,'2026-06-03 16:05:48',NULL),(24,'724','GT-58','Pending',3,'2026-06-05 10:09:37',NULL),(26,'724','GT-59','Pending',3,'2026-06-05 10:09:37',NULL),(27,'724','GT-57','Pending',3,'2026-06-05 10:09:37',NULL),(28,'724','GT-54','Pending',3,'2026-06-05 10:09:37',NULL),(29,'724','GT-55','Pending',3,'2026-06-05 10:09:37',NULL),(30,'724','GT-3','Pending',3,'2026-06-05 10:12:29',NULL),(31,'724','GT-61','Cleared',2,'2026-06-17 13:44:20','2026-06-19 12:49:15'),(32,'72401','GT-4','Pending',3,'2026-06-19 14:06:35',NULL),(33,'72401','GT-61','Pending',3,'2026-06-19 14:06:35',NULL),(34,'724','GT-4','Cleared',2,'2026-06-19 15:12:32','2026-06-19 15:24:33'),(35,'724','GT-1','Pending',3,'2026-06-19 15:18:50',NULL);
/*!40000 ALTER TABLE `clearance_subjects` ENABLE KEYS */;
UNLOCK TABLES;
SET @@SESSION.SQL_LOG_BIN = @MYSQLDUMP_TEMP_LOG_BIN;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

-- Dump completed on 2026-06-19 16:25:25
