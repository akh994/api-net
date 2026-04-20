CREATE TABLE IF NOT EXISTS `products` (
	`id` VARCHAR(36) NOT NULL PRIMARY KEY COMMENT 'Product unique identifier',
	`name` VARCHAR(255) NOT NULL COMMENT 'Product name',
	`description` TEXT COMMENT 'Product description',
	`price` DECIMAL(10,2) NOT NULL COMMENT 'Product price',
	`stock` INT NOT NULL DEFAULT 0 COMMENT 'Available stock',
	`category` VARCHAR(50) COMMENT 'Product category',
	`is_active` BOOLEAN NOT NULL DEFAULT true COMMENT 'Whether product is active',
	`created_at` DATETIME NOT NULL COMMENT 'Creation timestamp',
	`updated_at` DATETIME NOT NULL COMMENT 'Last update timestamp'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
