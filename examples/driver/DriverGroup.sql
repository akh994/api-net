CREATE TABLE `mt_group_periode` (
  `id` int PRIMARY KEY,
  `periode` varchar(6)
);

CREATE TABLE `mt_driver_group` (
  `id` int PRIMARY KEY,
  `pool_id` varchar(3),
  `periode_id` int,
  `driver_id` varchar(10),
  `company_id` varchar(5),
  `vehicle_type_id` int,
  `work_day` int,
  `income` decimal,
  `bonus` decimal
);

CREATE TABLE `tr_driver_group_member` (
  `id` int PRIMARY KEY,
  `group_id` int,
  `driver_id` varchar(10),
  `work_day` int,
  `average_income` decimal,
  `vehicle_type_id` int
);

CREATE TABLE `tr_insentif_group` (
  `id` int PRIMARY KEY,
  `periode_id` int,
  `group_id` int,
  `member_count` int,
  `insentif_group` decimal,
  `insentif_member` decimal
);

CREATE TABLE `conf_group` (
  `id` int PRIMARY KEY,
  `amount_insentif` decimal,
  `amount_insentif_member` decimal,
  `periode_from` varchar(6),
  `periode_until` varchar(6)
);

CREATE TABLE `tr_status_group` (
  `id` int PRIMARY KEY,
  `has_ops_process` bit,
  `has_adm_process` bit,
  `has_lock_periode` bit
);

CREATE TABLE `tr_bon_putih` (
  `id` int PRIMARY KEY,
  `periode` varchar(6),
  `insentif_group` decimal,
  `insentif_member` decimal
);
