-- Additional entities for AthleteManagement system
-- This schema will be used to test incremental updates

CREATE TABLE `coach` (
  `id` int PRIMARY KEY,
  `name` varchar(50),
  `email` varchar(100),
  `phone` varchar(20),
  `specialization` varchar(50),
  `hire_date` datetime,
  `is_active` int
);

CREATE TABLE `equipment` (
  `id` int PRIMARY KEY,
  `equipment_name` varchar(50),
  `equipment_type` varchar(30),
  `quantity` int,
  `location_id` int,
  `purchase_date` datetime,
  `condition_status` varchar(20),
  `create_at` datetime,
  `user_id` varchar(30)
);

-- Foreign key relationships
ALTER TABLE `equipment` ADD FOREIGN KEY (`location_id`) REFERENCES `location_training` (`id`);
