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
-- Table structure for table `clearance_organization`
--

DROP TABLE IF EXISTS `clearance_organization`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `clearance_organization` (
  `id` int NOT NULL AUTO_INCREMENT,
  `student_number` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL,
  `position` varchar(200) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `status` varchar(20) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'Pending',
  `period_id` int DEFAULT NULL,
  `requested_at` datetime DEFAULT NULL,
  `signed_at` datetime DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_co` (`student_number`,`position`,`period_id`),
  KEY `idx_clearance_org_student` (`student_number`),
  KEY `idx_clearance_org_name` (`position`),
  KEY `status` (`status`)
) ENGINE=InnoDB AUTO_INCREMENT=39 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `clearance_organization`
--

LOCK TABLES `clearance_organization` WRITE;
/*!40000 ALTER TABLE `clearance_organization` DISABLE KEYS */;
INSERT INTO `clearance_organization` VALUES (6,'724','SSG Treasurer','Cleared',4,'2026-06-03 16:05:48',NULL),(7,'724','Organization Adviser','Pending',4,'2026-06-03 16:05:48',NULL),(8,'724','Class Adviser','Cleared',4,'2026-06-03 16:05:48',NULL),(9,'72401','SSG Treasurer','Pending',4,'2026-06-03 16:05:48',NULL),(10,'72401','Organization Adviser','Pending',4,'2026-06-03 16:05:48',NULL),(20,'724','Class Adviser','Cleared',3,'2026-06-03 16:05:48',NULL),(22,'724','SSG Treasurer','Cleared',3,'2026-06-03 16:05:48',NULL),(24,'724','Class Adviser','Cleared',2,'2026-06-03 16:05:48','2026-06-17 14:06:41'),(26,'724','Department Chairperson','Pending',3,'2026-06-05 10:13:55',NULL),(27,'724','SSG Treasurer','Pending',2,'2026-06-19 15:20:33',NULL),(28,'724','Department Chairperson','Pending',2,'2026-06-05 19:19:10',NULL),(29,'724','Organization Adviser','Pending',2,'2026-06-05 19:19:22',NULL),(30,'724','Computer Laboratory In-Charge','Pending',2,'2026-06-05 19:19:29',NULL),(34,'72401','Class Adviser','Declined',3,'2026-06-17 14:19:31','2026-06-19 15:25:55'),(35,'72401','Class Adviser','Pending',4,'2026-06-17 14:59:23',NULL),(36,'72401','Class Adviser','Pending',2,'2026-06-19 13:16:45',NULL),(37,'724','Computer Laboratory In-Charge','Pending',3,'2026-06-19 15:19:14',NULL),(38,'724','Organization Adviser','Pending',3,'2026-06-19 15:19:26',NULL);
/*!40000 ALTER TABLE `clearance_organization` ENABLE KEYS */;
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

-- Dump completed on 2026-06-19 16:25:26
