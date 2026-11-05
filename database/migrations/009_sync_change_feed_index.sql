-- Keep filtered company feed pages bounded and ordered as each tenant grows.
CREATE INDEX sync_change_feed_page_idx
  ON sync.change_log(tenant_id, company_id, entity_type, company_sequence);
