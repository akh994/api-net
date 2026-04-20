CREATE TABLE `class_category` (
  `id` int PRIMARY KEY,
  `description` varchar(20)
);

CREATE TABLE `fee` (
  `id` int PRIMARY KEY,
  `calss_id` int,
  `valid_from` datetime,
  `valid_until` datetime,
  `fees` decimal,
  `max_disc` decimal,
  `create_at` datetime,
  `user_id` varchar(30)
);

CREATE TABLE `athlete` (
  `id` int PRIMARY KEY,
  `name` varchar(50),
  `nick_name` varchar(50),
  `birth_place` varchar(30),
  `birth_date` datetime,
  `gender` int,
  `class_id` int,
  `fee_id` int
);

CREATE TABLE `location_training` (
  `id` int PRIMARY KEY,
  `location_name` varchar(30)
);

CREATE TABLE `class_location` (
  `id` int PRIMARY KEY,
  `class_id` int,
  `location_id` int,
  `schedule_start` varchar(5),
  `schedule_end` varchar(5),
  `create_at` datetime,
  `user_id` varchar(30)
);

CREATE TABLE `athlete_class_location` (
  `id` int PRIMARY KEY,
  `athlete_id` int,
  `class_location_id` int
);

CREATE TABLE `payment` (
  `id` int PRIMARY KEY,
  `athlete_class_location_id` int,
  `periode` varchar(6),
  `amount` decimal,
  `payment_methode` int,
  `create_at` datetime,
  `user_id` varchar(30)
);

CREATE TABLE `absence` (
  `id` int PRIMARY KEY,
  `athlete_class_location_id` int,
  `present` int,
  `create_at` datetime,
  `user_id` varchar(30)
);

ALTER TABLE `fee` ADD FOREIGN KEY (`calss_id`) REFERENCES `class_category` (`id`);

ALTER TABLE `class_location` ADD FOREIGN KEY (`class_id`) REFERENCES `class_category` (`id`);

ALTER TABLE `class_location` ADD FOREIGN KEY (`location_id`) REFERENCES `location_training` (`id`);

ALTER TABLE `athlete_class_location` ADD FOREIGN KEY (`athlete_id`) REFERENCES `athlete` (`id`);

ALTER TABLE `athlete_class_location` ADD FOREIGN KEY (`class_location_id`) REFERENCES `class_location` (`id`);

ALTER TABLE `payment` ADD FOREIGN KEY (`athlete_class_location_id`) REFERENCES `athlete_class_location` (`id`);

ALTER TABLE `absence` ADD FOREIGN KEY (`athlete_class_location_id`) REFERENCES `athlete_class_location` (`id`);

ALTER TABLE `class_locations` ADD FOREIGN KEY (`user_id`) REFERENCES `class_location` (`class_id`);
