# Integration event conventions

Event names use `module.entity.action.v1`, for example:
- `inventory.stock-movement.posted.v1`
- `purchasing.goods-receipt.accepted.v1`
- `finance.journal.posted.v1`

Every event envelope should include:
- event ID (UUID);
- tenant ID and company ID;
- aggregate type and ID;
- aggregate version;
- occurred-at timestamp (UTC);
- correlation ID and causation ID;
- schema version;
- payload.

Events are integration contracts, not direct database table dumps. Consumers must be idempotent and validate tenant/company scope.
