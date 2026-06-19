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
-- Table structure for table `clearance_messages`
--

DROP TABLE IF EXISTS `clearance_messages`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `clearance_messages` (
  `id` int NOT NULL AUTO_INCREMENT,
  `sender_id` int NOT NULL,
  `student_number` varchar(50) NOT NULL,
  `clearance_type` varchar(20) NOT NULL,
  `clearance_key` varchar(200) NOT NULL,
  `message` text NOT NULL,
  `sent_at` datetime DEFAULT CURRENT_TIMESTAMP,
  `is_read` tinyint NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `idx_chat` (`student_number`,`clearance_type`,`clearance_key`)
) ENGINE=InnoDB AUTO_INCREMENT=61 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;

--
-- Dumping data for table `clearance_messages`
--

LOCK TABLES `clearance_messages` WRITE;
/*!40000 ALTER TABLE `clearance_messages` DISABLE KEYS */;
INSERT INTO `clearance_messages` VALUES (1,3,'724','subject','GT-1','shdfhasdjfhal','2026-05-13 18:54:44',1),(2,4,'724','subject','GT-2','sdfkjahskjdfhljash','2026-05-14 06:24:13',1),(3,3,'724','subject','GT-2','nsdajshkjdhsajk','2026-05-14 09:33:20',1),(4,3,'724','subject','GT-2','jskfjejhfjlkeh','2026-05-14 09:37:49',1),(5,3,'724','subject','GT-1','dfjgsjhfdgjlkshdf','2026-05-14 14:14:57',0),(6,3,'724','org','SSG Treasurer','hi?','2026-05-20 13:38:14',0),(7,3,'724','subject','GT-1','sir?','2026-05-20 13:38:25',0),(8,3,'724','org','SSG Treasurer','ouy','2026-05-20 13:49:04',0),(9,3,'724','org','Organization Adviser','sir?','2026-05-20 13:51:06',0),(10,3,'724','org','Class Adviser','maam, paaprove po','2026-05-20 14:13:09',1),(11,3,'724','org','SSG Treasurer','iapproved ba','2026-05-20 17:22:39',0),(12,3,'724','org','SSG Treasurer','garaa','2026-05-20 17:22:42',0),(13,3,'724','org','SSG Treasurer','ok','2026-05-20 17:31:22',0),(14,3,'724','subject','GT-2','mgfgdfgh','2026-05-24 23:43:47',1),(15,3,'724','subject','GT-4','bvghfjgdjg','2026-05-24 23:44:56',1),(16,4,'724','subject','GT-4','asjdfjasbdjbfahd','2026-05-24 23:46:26',1),(17,3,'724','subject','GT-4','sdfajshdfkja','2026-05-25 00:11:02',1),(18,4,'724','subject','GT-4','fjsdjfahsdfljah','2026-05-25 00:11:59',1),(19,3,'724','subject','GT-4','hgfkfjgfjghf','2026-05-25 00:27:56',1),(20,3,'724','subject','GT-4','sdjfkahsjkdfhajsdhf','2026-05-25 00:38:43',1),(21,3,'724','org','SSG Treasurer','di day ko','2026-05-28 14:16:08',0),(22,3,'724','org','SSG Treasurer',':P','2026-05-28 14:16:13',0),(23,3,'724','subject','GT-4','hjkhjghfghfjghf','2026-06-04 12:45:40',1),(24,3,'724','subject','GT-61','hiiiii asa na ka?','2026-06-17 13:44:35',1),(25,4,'724','subject','GT-61','ejfhgdjhjgfs','2026-06-17 13:54:45',1),(26,3,'724','subject','GT-61','ahgsdkjgfakjsgdf','2026-06-17 13:55:18',1),(27,9,'72401','org','Class Adviser','sdfhgkjsdhfjghs','2026-06-17 13:59:09',1),(28,9,'72401','org','Class Adviser','gfhgfgfghdgfdghf','2026-06-17 14:05:58',1),(29,9,'72401','org','Class Adviser','jhsafjkhkjafhkjhfk','2026-06-17 14:09:48',1),(30,9,'72401','org','Class Adviser','maam','2026-06-17 16:32:13',1),(31,5,'72401','org','Class Adviser','yes','2026-06-17 17:00:46',1),(32,9,'72401','org','Class Adviser','heloo','2026-06-17 17:01:52',1),(33,9,'72401','org','Class Adviser','ksjhff','2026-06-17 17:14:57',1),(34,9,'72401','org','Class Adviser','jehfjshdfkjahd','2026-06-18 09:21:37',1),(35,5,'72401','org','Class Adviser','dsfhajhdfjhakjsdh','2026-06-18 09:22:01',1),(36,9,'72401','org','Class Adviser','djfghksjhdfjsh','2026-06-18 09:31:30',1),(37,9,'72401','org','Class Adviser','dbjasjdhfajhsdjfag','2026-06-18 20:48:52',1),(38,9,'72401','org','Class Adviser','jjkwefhjkshdkjfhasjkdhf','2026-06-18 21:00:21',1),(39,5,'72401','org','Class Adviser','jhdjhefjkshjkd','2026-06-18 21:18:51',1),(40,9,'72401','org','Class Adviser','shsdfjhskjdfhjskd','2026-06-18 21:19:23',1),(41,9,'72401','org','Class Adviser','fjgleuge','2026-06-19 12:50:26',1),(42,9,'72401','org','Class Adviser','maam','2026-06-19 13:16:51',1),(43,9,'72401','org','Class Adviser','ghfhgfghfgfhgfhgh','2026-06-19 13:29:17',1),(44,9,'72401','org','Class Adviser','yow','2026-06-19 13:55:23',1),(45,5,'72401','org','Class Adviser','ahdsgfasgdhfgasjd','2026-06-19 14:01:10',1),(46,9,'72401','org','Class Adviser','jdajhdfjahjskdfhakd','2026-06-19 14:02:39',1),(47,9,'72401','subject','GT-4','jhdfkjshdjfhakjdh','2026-06-19 14:06:44',0),(48,3,'724','org','SSG Treasurer','cfgdmh','2026-06-19 14:12:18',0),(49,9,'72401','org','Class Adviser','khsdhfkjashdjkfha','2026-06-19 14:15:12',1),(50,5,'72401','org','Class Adviser','kfojci','2026-06-19 14:16:53',1),(51,9,'72401','org','Class Adviser','hkjfjjklkj','2026-06-19 14:18:05',1),(52,9,'72401','org','Class Adviser','hellowwwww','2026-06-19 14:24:00',1),(53,5,'72401','org','Class Adviser','1231','2026-06-19 14:24:56',1),(54,9,'72401','org','Class Adviser','fwfwessf','2026-06-19 14:40:04',1),(55,9,'72401','org','Class Adviser','asasddw','2026-06-19 14:46:17',1),(56,9,'72401','org','Class Adviser','kani','2026-06-19 14:59:31',1),(57,9,'72401','org','Class Adviser','wazap','2026-06-19 15:10:50',1),(58,3,'724','subject','GT-4','jklkjhkjhjh','2026-06-19 15:12:41',1),(59,4,'724','subject','GT-4','jdsfhkjashdjkf','2026-06-19 15:21:33',1),(60,3,'724','subject','GT-4','jsdhfkjashfjka','2026-06-19 15:21:57',1);
/*!40000 ALTER TABLE `clearance_messages` ENABLE KEYS */;
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

-- Dump completed on 2026-06-19 16:25:27
