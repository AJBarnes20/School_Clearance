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
-- Table structure for table `users`
--

DROP TABLE IF EXISTS `users`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `users` (
  `id` int NOT NULL AUTO_INCREMENT,
  `first_name` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `middle_initial` varchar(10) COLLATE utf8mb4_unicode_ci DEFAULT '',
  `last_name` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
  `suffix_name` varchar(20) COLLATE utf8mb4_unicode_ci DEFAULT '',
  `email` varchar(100) COLLATE utf8mb4_unicode_ci NOT NULL,
  `password` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL,
  `id_number` varchar(50) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `curriculum_id` int DEFAULT NULL,
  `role` varchar(20) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'Pending',
  `is_active` tinyint(1) NOT NULL DEFAULT '0',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `otp_code` varchar(6) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `otp_expiry` datetime DEFAULT NULL,
  `reset_token` varchar(64) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `reset_expiry` datetime DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `email` (`email`),
  KEY `idx_users_role` (`role`),
  KEY `idx_users_id_number` (`id_number`),
  KEY `fk_users_curriculum` (`curriculum_id`),
  CONSTRAINT `fk_users_curriculum` FOREIGN KEY (`curriculum_id`) REFERENCES `curriculum` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB AUTO_INCREMENT=13 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `users`
--

LOCK TABLES `users` WRITE;
/*!40000 ALTER TABLE `users` DISABLE KEYS */;
INSERT INTO `users` VALUES (1,'Admin','','User','','admin@clearance.edu','$2a$11$QK8J2xKJQK8J2xKJQK8J2xKJQK8J2xKJQK8J2xKJQK8J2xKJQK8J2','ADMIN001',NULL,'Staff',1,'2026-05-09 22:29:32',NULL,NULL,NULL,NULL),(2,'Althea Jean','','Barnes','','altheajeanbarnes@gmail.com','$2a$11$hgv8bx.F3SjXRuRNYY4p6ecz.n28uQKjEhElAXAHseiRmc.wi9AVW','7240185',NULL,'Admin',1,'2026-05-10 00:02:08',NULL,NULL,NULL,NULL),(3,'Melanie','T','Dinglasa','','melanietocodinglasa@gmail.com','$2a$11$NYbBswW0X2olzBLbE5dOEehvMEWcTvT4UHBISkaRcNHkzZObXzgZy','724',NULL,'Student',1,'2026-05-10 07:43:33','701700','2026-05-20 16:12:51','76713cc58313421fa1609d837e8c7766','2026-05-20 16:19:04'),(4,'Mila','','Villamor','','milamarie27@gmail.com','$2a$11$6TdYlcB9RzW.FiEzi9gIZ.2hQc0FrEuQNfYw92bTiHdNO8lFXOG/G','7240',NULL,'Instructor',1,'2026-05-10 07:44:41',NULL,NULL,NULL,NULL),(5,'Aesha','','TERRAZA','','jeriza_marie86@yahoo.com','$2a$11$bxVPp6ASH1I.GXFGBDJPT.8NXWgtthaoIa0e1rnGLJn1WSqRDwojO','72',NULL,'Staff',1,'2026-05-11 02:23:40',NULL,NULL,NULL,NULL),(6,'Mel','T.','Dinglasa','','melaniedinglasa12@gmail.com','$2a$11$N.PFqNG.xbS3v9eAgs7bZO/fdNBrENI23bKuPVm/r.mfjIN9d5gM.','7240424',NULL,'Student',1,'2026-05-14 10:00:11',NULL,NULL,NULL,NULL),(7,'Mela','T.','Denham','','mdinglasa@gmail.com','$2a$11$aMYfebC2ftAIeq.1jRKjRuDScP8SfUl7yy02.W6U0P1NMrtU/qcDm','12345',NULL,'Instructor',1,'2026-05-14 10:02:05',NULL,NULL,NULL,NULL),(8,'Ian','A.','Comision','','iancomision@gmail.com','$2a$11$Qx0T6SE8w3k0rDeS52GT.eiBWyn1plTcjYmF/enu8A1u44VI/wQ9W','67890',NULL,'Instructor',0,'2026-05-14 13:56:01',NULL,NULL,NULL,NULL),(9,'Sheena',NULL,'Melgar',NULL,'sheenamelgar@gmail.com','$2a$11$.eGOzvZ29gy/fzV4EhzeCecGLlkjTDNZO/MmmXDklE.Aax5lNeCdu','72401',2,'Student',1,'2026-05-24 12:18:14',NULL,NULL,NULL,NULL),(10,'Jayvince','S.','Singco','','','$2a$11$Sa407pAgMXFojHJN0KEV7uo7wfkyKINR516NW30U7wOcuwgDRhjKu','7241',NULL,'Student',1,'2026-05-28 10:52:02',NULL,NULL,NULL,NULL),(11,'Ronald','C.','Arias',NULL,'ronaldarias1618@gmail.com','$2a$11$zk5Xb9yk/INCveoGin/h2eI2..RIfZYP224TvAAXlq411nfktqX/C','7241241',2,'Student',1,'2026-05-28 11:26:33',NULL,NULL,NULL,NULL);
/*!40000 ALTER TABLE `users` ENABLE KEYS */;
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
