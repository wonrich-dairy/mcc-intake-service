-- The auth service keeps its own database: user credentials must not sit in a schema every
-- other service's connection string can already reach (SCRUM-34).
--
-- docker-compose's MYSQL_DATABASE/MYSQL_USER only ever creates mcc_intake and grants mcc_user
-- on that one schema, so without this the auth service starts, tries to migrate, and is refused.
-- Runs once, when the data volume is first initialised.
CREATE DATABASE IF NOT EXISTS `wonrich_auth`
  CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;

GRANT ALL PRIVILEGES ON `wonrich_auth`.* TO 'mcc_user'@'%';
FLUSH PRIVILEGES;
