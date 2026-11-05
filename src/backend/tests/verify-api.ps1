param()

$ErrorActionPreference = "Stop"

$apiUrl = "http://127.0.0.1:5000"
$apiDir = "c:\Users\Allah\Desktop\uniersal erp\universal-erp-core\src\backend\src\UniversalErp.Api"
$apiDll = Join-Path $apiDir "bin\Debug\net10.0\UniversalErp.Api.dll"

Write-Host "Starting API process in $apiDir..."
$process = Start-Process -FilePath "dotnet" -ArgumentList "`"$apiDll`" --urls $apiUrl --environment Development" -WorkingDirectory $apiDir -PassThru -NoNewWindow

try {
    # 1. Wait for API to become ready
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            $health = Invoke-RestMethod -Uri "$apiUrl/health" -Method Get -TimeoutSec 2
            if ($health -eq "Healthy" -or $health.status -eq "Healthy") {
                $ready = $true
                break
            }
        } catch {}
    }

    if (-not $ready) {
        throw "API failed to start within 15 seconds."
    }
    Write-Host "[PASS] API is Healthy on $apiUrl"

    # 2. Check /api/v1/plans
    $plans = Invoke-RestMethod -Uri "$apiUrl/api/v1/plans" -Method Get
    if ($plans.Count -lt 3) {
        throw "Expected at least 3 plans, got $($plans.Count)"
    }
    Write-Host "[PASS] Plans endpoint returned $($plans.Count) active plans."

    # 3. Test Tenant Registration
    $idempotencyKey = [Guid]::NewGuid().ToString()
    $body = @{
        tenantCode = "test-corp"
        displayName = "Test Corp Pakistan"
        legalName = "Test Corp (Pvt) Limited"
        planCode = "professional"
        adminFullName = "Zubair Admin"
        adminEmail = "admin@test-corp.pk"
        adminPhone = "+923001112233"
        countryCode = "PK"
        baseCurrencyCode = "PKR"
        timezone = "Asia/Karachi"
    } | ConvertTo-Json

    $headers = @{
        "Idempotency-Key" = $idempotencyKey
        "Content-Type" = "application/json"
    }

    try {
        $resp = Invoke-RestMethod -Uri "$apiUrl/api/v1/tenants/register" -Method Post -Headers $headers -Body $body
    } catch {
        if ($_.Exception.Response) {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $errDetail = $reader.ReadToEnd()
            Write-Host "Registration error response: $errDetail"
        }
        throw
    }
    $tenantId = $resp.tenantId
    if (-not $tenantId) {
        throw "Registration did not return tenantId."
    }
    Write-Host "[PASS] Successfully registered tenant $tenantId ($($resp.tenantCode))"
    Write-Host "       Company: $($resp.company.name) ($($resp.company.companyCode))"
    Write-Host "       Subscription: $($resp.subscription.planName)"
    Write-Host "       Entitlements: $($resp.entitlements.Count) features snapshot"

    # 4. Test Idempotency Replay
    $replayResp = Invoke-RestMethod -Uri "$apiUrl/api/v1/tenants/register" -Method Post -Headers $headers -Body $body
    if ($replayResp.tenantId -ne $tenantId) {
        throw "Idempotency replay returned different tenantId: $($replayResp.tenantId)"
    }
    Write-Host "[PASS] Idempotent registration replay succeeded with identical tenantId."

    # 5. Test Idempotency Key Reuse Conflict
    $conflictBody = @{
        tenantCode = "another-corp"
        displayName = "Another Corp"
        planCode = "starter"
        adminFullName = "Another Admin"
        adminEmail = "another@corp.pk"
    } | ConvertTo-Json

    $conflictDetected = $false
    try {
        Invoke-RestMethod -Uri "$apiUrl/api/v1/tenants/register" -Method Post -Headers $headers -Body $conflictBody
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 409) {
            $conflictDetected = $true
        }
    }
    if (-not $conflictDetected) {
        throw "Expected 409 Conflict when reusing Idempotency-Key with different payload."
    }
    Write-Host "[PASS] Idempotency key conflict correctly returned 409 Conflict."

    # 6. Test Duplicate Tenant Code Conflict
    $dupBody = @{
        tenantCode = "test-corp"
        displayName = "Duplicate Corp"
        planCode = "starter"
        adminFullName = "Dup Admin"
        adminEmail = "dup@corp.pk"
    } | ConvertTo-Json

    $dupHeaders = @{
        "Idempotency-Key" = [Guid]::NewGuid().ToString()
        "Content-Type" = "application/json"
    }

    $dupDetected = $false
    try {
        Invoke-RestMethod -Uri "$apiUrl/api/v1/tenants/register" -Method Post -Headers $dupHeaders -Body $dupBody
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 409) {
            $dupDetected = $true
        }
    }
    if (-not $dupDetected) {
        throw "Expected 409 Conflict when using existing tenantCode."
    }
    Write-Host "[PASS] Duplicate tenantCode correctly rejected with 409 Conflict."

    # 7. Test Lookup endpoints
    $fetchedTenant = Invoke-RestMethod -Uri "$apiUrl/api/v1/tenants/$tenantId" -Method Get
    if ($fetchedTenant.tenantId -ne $tenantId) {
        throw "GET /tenants/{tenantId} returned wrong tenant."
    }
    Write-Host "[PASS] GET /api/v1/tenants/$tenantId verified."

    $byCode = Invoke-RestMethod -Uri "$apiUrl/api/v1/tenants/by-code/test-corp" -Method Get
    if ($byCode.tenantId -ne $tenantId) {
        throw "GET /tenants/by-code/test-corp returned wrong tenant."
    }
    Write-Host "[PASS] GET /api/v1/tenants/by-code/test-corp verified."

    # 8. Finance & Double-Entry Accounting Verification
    $accounts = Invoke-RestMethod -Uri "$apiUrl/api/v1/finance/accounts" -Method Get
    if ($accounts.Count -lt 10) {
        throw "Expected at least 10 seeded accounts, got $($accounts.Count)"
    }
    Write-Host "[PASS] Chart of Accounts verified: $($accounts.Count) active accounts loaded."

    $bankAcc = $accounts | Where-Object { $_.accountCode -eq "1020" } | Select-Object -First 1
    $salesAcc = $accounts | Where-Object { $_.accountCode -eq "4010" } | Select-Object -First 1

    $periods = Invoke-RestMethod -Uri "$apiUrl/api/v1/finance/periods" -Method Get
    $openPeriod = $periods | Where-Object { $_.status -eq "open" } | Select-Object -First 1
    if (-not $openPeriod) {
        throw "No open fiscal period found."
    }
    Write-Host "[PASS] Open fiscal period found: $($openPeriod.name) ($($openPeriod.periodId))"

    # Test Unbalanced Journal Rejection
    $unbalancedReq = @{
        journalNo = "JRN-UNBALANCED"
        journalDate = (Get-Date).ToString("yyyy-MM-dd")
        fiscalPeriodId = $openPeriod.periodId
        description = "Unbalanced test entry"
        functionalCurrency = "PKR"
        sourceModule = "manual"
        sourceType = "test"
        sourceId = "1"
        lines = @(
            @{
                accountId = $bankAcc.accountId
                description = "Bank debit"
                transactionCurrency = "PKR"
                transactionAmount = 50000
                exchangeRate = 1
                debitFunctional = 50000
                creditFunctional = 0
            },
            @{
                accountId = $salesAcc.accountId
                description = "Sales credit mismatch"
                transactionCurrency = "PKR"
                transactionAmount = 40000
                exchangeRate = 1
                debitFunctional = 0
                creditFunctional = 40000
            }
        )
    } | ConvertTo-Json -Depth 5

    $unbalancedHeaders = @{
        "Idempotency-Key" = [Guid]::NewGuid().ToString()
        "Content-Type" = "application/json"
    }

    $unbalancedRejected = $false
    try {
        Invoke-RestMethod -Uri "$apiUrl/api/v1/finance/journals" -Method Post -Headers $unbalancedHeaders -Body $unbalancedReq
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 422) {
            $unbalancedRejected = $true
        }
    }
    if (-not $unbalancedRejected) {
        throw "Expected 422 Unprocessable Entity for unbalanced journal."
    }
    Write-Host "[PASS] Unbalanced journal correctly rejected with 422 Unprocessable Entity."

    # Test Balanced Double-Entry Journal Posting
    $balancedJournalNo = "JRN-" + [Guid]::NewGuid().ToString().Substring(0, 8).ToUpper()
    $balancedKey = [Guid]::NewGuid().ToString()
    $balancedReq = @{
        journalNo = $balancedJournalNo
        journalDate = (Get-Date).ToString("yyyy-MM-dd")
        fiscalPeriodId = $openPeriod.periodId
        description = "November cash sales deposit"
        functionalCurrency = "PKR"
        sourceModule = "pos"
        sourceType = "daily_close"
        sourceId = "POS-CLOSE-01"
        lines = @(
            @{
                accountId = $bankAcc.accountId
                description = "Bank operating debit"
                transactionCurrency = "PKR"
                transactionAmount = 75000
                exchangeRate = 1
                debitFunctional = 75000
                creditFunctional = 0
            },
            @{
                accountId = $salesAcc.accountId
                description = "Product sales revenue credit"
                transactionCurrency = "PKR"
                transactionAmount = 75000
                exchangeRate = 1
                debitFunctional = 0
                creditFunctional = 75000
            }
        )
    } | ConvertTo-Json -Depth 5

    $balancedHeaders = @{
        "Idempotency-Key" = $balancedKey
        "Content-Type" = "application/json"
    }

    $postedJournal = Invoke-RestMethod -Uri "$apiUrl/api/v1/finance/journals" -Method Post -Headers $balancedHeaders -Body $balancedReq
    if ($postedJournal.totalDebit -ne 75000 -or $postedJournal.totalCredit -ne 75000) {
        throw "Posted journal totals mismatch."
    }
    Write-Host "[PASS] Balanced journal $balancedJournalNo successfully committed to General Ledger (Rs $($postedJournal.totalDebit))."

    # Test Trial Balance Verification
    $tb = Invoke-RestMethod -Uri "$apiUrl/api/v1/finance/trial-balance" -Method Get
    $tbTotalDebit = ($tb | Measure-Object -Property totalDebit -Sum).Sum
    $tbTotalCredit = ($tb | Measure-Object -Property totalCredit -Sum).Sum
    if ([Math]::Abs($tbTotalDebit - $tbTotalCredit) -gt 0.001) {
        throw "Trial Balance is OUT OF BALANCE: Debit = $tbTotalDebit, Credit = $tbTotalCredit"
    }
    Write-Host "[PASS] Trial Balance verified mathematically balanced: Debits = Rs $tbTotalDebit, Credits = Rs $tbTotalCredit"

    # ─────────────────────────────────────────────────────────────────────────────
    # Phase 3: Inventory Movements, Transfers, Adjustments, and Supply Chain Outbox
    # ─────────────────────────────────────────────────────────────────────────────
    Write-Host "`n--- Phase 3 Verification: Inventory Transfers, Adjustments & Outbox ---"

    # 9. Create Product and Variant
    $prodBody = @{
        productCode = "PROD-TEST-P3"
        name = "Basmati Rice 5kg"
        productType = "stock"
        description = "Phase 3 test product"
    } | ConvertTo-Json
    $prodHeaders = @{ "Idempotency-Key" = [Guid]::NewGuid().ToString(); "Content-Type" = "application/json" }
    $prod = Invoke-RestMethod -Uri "$apiUrl/api/v1/products" -Method Post -Headers $prodHeaders -Body $prodBody
    Write-Host "[PASS] Created product $($prod.productCode) ($($prod.productId))"

    $uomId = "00000000-0000-4000-8000-000000000006"
    $varBody = @{
        productId = $prod.productId
        sku = "SKU-RICE-5KG"
        variantName = "Standard 5kg Bag"
        baseUnitOfMeasureId = $uomId
        trackingMode = "none"
        costingMethod = "fifo"
    } | ConvertTo-Json
    $variant = Invoke-RestMethod -Uri "$apiUrl/api/v1/products/$($prod.productId)/variants" -Method Post -Headers @{"Content-Type"="application/json"} -Body $varBody
    Write-Host "[PASS] Created variant $($variant.sku) ($($variant.variantId))"

    # 10. Create Warehouses and Bins
    $wh1Body = @{ code = "WH-KHI-01"; name = "Karachi Main Hub" } | ConvertTo-Json
    $wh1 = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/warehouses" -Method Post -Headers @{"Content-Type"="application/json"} -Body $wh1Body
    $bin1Body = @{ code = "BIN-KHI-A1"; binType = "storage" } | ConvertTo-Json
    $bin1 = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/warehouses/$($wh1.warehouseId)/bins" -Method Post -Headers @{"Content-Type"="application/json"} -Body $bin1Body
    Write-Host "[PASS] Created source warehouse $($wh1.code) and bin $($bin1.code)"

    $wh2Body = @{ code = "WH-LHR-01"; name = "Lahore Regional Hub" } | ConvertTo-Json
    $wh2 = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/warehouses" -Method Post -Headers @{"Content-Type"="application/json"} -Body $wh2Body
    $bin2Body = @{ code = "BIN-LHR-B1"; binType = "storage" } | ConvertTo-Json
    $bin2 = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/warehouses/$($wh2.warehouseId)/bins" -Method Post -Headers @{"Content-Type"="application/json"} -Body $bin2Body
    Write-Host "[PASS] Created destination warehouse $($wh2.code) and bin $($bin2.code)"

    # 11. Post Initial Stock Receipt (50 units)
    $receiptReq = @{
        variantId = $variant.variantId
        warehouseId = $wh1.warehouseId
        binId = $bin1.binId
        quantity = 50
        unitOfMeasureId = $uomId
        note = "Initial batch receipt"
    } | ConvertTo-Json
    $rcpt = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/receipts" -Method Post -Headers @{"Idempotency-Key"=[Guid]::NewGuid().ToString(); "Content-Type"="application/json"} -Body $receiptReq
    Write-Host "[PASS] Posted initial stock receipt of 50 EA into $($bin1.code)"

    # 12. Post Stock Transfer (20 units from KHI to LHR)
    $transferReq = @{
        variantId = $variant.variantId
        sourceWarehouseId = $wh1.warehouseId
        sourceBinId = $bin1.binId
        destinationWarehouseId = $wh2.warehouseId
        destinationBinId = $bin2.binId
        quantity = 20
        unitOfMeasureId = $uomId
        note = "Transfer KHI to LHR"
    } | ConvertTo-Json
    $transfer = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/transfers" -Method Post -Headers @{"Idempotency-Key"=[Guid]::NewGuid().ToString(); "Content-Type"="application/json"} -Body $transferReq
    Write-Host "[PASS] Posted stock transfer of 20 EA from $($bin1.code) to $($bin2.code)"

    # Verify Stock Availability after Transfer
    $availAfterTransfer = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/availability?variantId=$($variant.variantId)" -Method Get
    $khiStock = ($availAfterTransfer | Where-Object { $_.binId -eq $bin1.binId }).onHandQuantity
    $lhrStock = ($availAfterTransfer | Where-Object { $_.binId -eq $bin2.binId }).onHandQuantity
    if ($khiStock -ne 30 -or $lhrStock -ne 20) {
        throw "Stock transfer balance mismatch: Expected KHI=30, LHR=20, got KHI=$khiStock, LHR=$lhrStock"
    }
    Write-Host "[PASS] Stock balances verified after transfer: KHI = $khiStock EA, LHR = $lhrStock EA."

    # 13. Post Stock Adjustment (-2 units damaged)
    $adjReq = @{
        variantId = $variant.variantId
        warehouseId = $wh1.warehouseId
        binId = $bin1.binId
        quantityDelta = -2
        unitOfMeasureId = $uomId
        reasonCode = "damaged_goods"
        note = "Damaged during handling write-off"
    } | ConvertTo-Json
    $adj = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/adjustments" -Method Post -Headers @{"Idempotency-Key"=[Guid]::NewGuid().ToString(); "Content-Type"="application/json"} -Body $adjReq
    Write-Host "[PASS] Posted stock adjustment: delta = $($adj.quantityDelta) (reason: $($adj.reasonCode))"

    # Verify Stock Availability after Adjustment
    $availAfterAdj = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/availability?variantId=$($variant.variantId)" -Method Get
    $khiStockAdj = ($availAfterAdj | Where-Object { $_.binId -eq $bin1.binId }).onHandQuantity
    if ($khiStockAdj -ne 28) {
        throw "Stock adjustment mismatch: Expected KHI=28, got $khiStockAdj"
    }
    Write-Host "[PASS] Stock balance verified after adjustment: KHI = $khiStockAdj EA."

    # 14. Supply Chain Outbox Automation: Purchasing PO -> GRN -> Automated Stock & Double-Entry GL Posting
    $supBody = @{
        supplierCode = "SUP-ENGRO-P3"
        name = "Engro Foods Limited"
        currencyCode = "PKR"
        paymentTermsDays = 30
    } | ConvertTo-Json
    $sup = Invoke-RestMethod -Uri "$apiUrl/api/v1/purchasing/suppliers" -Method Post -Headers @{"Content-Type"="application/json"} -Body $supBody
    Write-Host "[PASS] Created supplier $($sup.supplierCode) ($($sup.name))"

    $poBody = @{
        poNumber = "PO-2026-P3-001"
        supplierId = $sup.supplierId
        warehouseId = $wh1.warehouseId
        orderDate = (Get-Date).ToString("yyyy-MM-dd")
        note = "Restock order for Basmati Rice"
        lines = @(
            @{
                variantId = $variant.variantId
                orderedQty = 10
                unitOfMeasureId = $uomId
                unitPrice = 1200
            }
        )
    } | ConvertTo-Json -Depth 5
    $po = Invoke-RestMethod -Uri "$apiUrl/api/v1/purchasing/purchase-orders" -Method Post -Headers @{"Content-Type"="application/json"} -Body $poBody
    Write-Host "[PASS] Created PO $($po.poNumber) in status $($po.orderStatus)"

    $poConfirmed = Invoke-RestMethod -Uri "$apiUrl/api/v1/purchasing/purchase-orders/$($po.purchaseOrderId)/confirm" -Method Post -Headers @{"Content-Type"="application/json"}
    Write-Host "[PASS] Confirmed PO $($poConfirmed.poNumber), status is $($poConfirmed.orderStatus)"

    # Post Goods Received Note (GRN) for 10 units at Rs 1,200/EA (Total Rs 12,000)
    $grnReq = @{
        clientTransactionId = [Guid]::NewGuid().ToString()
        grnNumber = "GRN-2026-P3-001"
        purchaseOrderId = $po.purchaseOrderId
        receivedDate = (Get-Date).ToString("yyyy-MM-dd")
        lines = @(
            @{
                poLineNumber = 1
                variantId = $variant.variantId
                binId = $bin1.binId
                receivedQty = 10
                unitOfMeasureId = $uomId
                unitPrice = 1200
            }
        )
    } | ConvertTo-Json -Depth 5
    $grn = Invoke-RestMethod -Uri "$apiUrl/api/v1/purchasing/grns" -Method Post -Headers @{"Content-Type"="application/json"} -Body $grnReq
    Write-Host "[PASS] Posted GRN $($grn.grnNumber) for $($grn.lines[0].receivedQty) EA at Rs $($grn.lines[0].unitPrice)/EA."

    # Process outbox (in case background run or synchronous trigger)
    $outboxResult = Invoke-RestMethod -Uri "$apiUrl/api/v1/purchasing/outbox/process" -Method Post -Headers @{"Content-Type"="application/json"}
    Write-Host "[PASS] Outbox processor completed: processed $($outboxResult.processedEvents) event(s), $($outboxResult.stockMovementsCreated) movement(s), $($outboxResult.journalsPosted) GL journal(s)."

    # Verify Stock Availability increased by 10 (from 28 to 38)
    $availAfterGrn = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/availability?variantId=$($variant.variantId)" -Method Get
    $khiStockGrn = ($availAfterGrn | Where-Object { $_.binId -eq $bin1.binId }).onHandQuantity
    if ($khiStockGrn -ne 38) {
        throw "Stock after GRN mismatch: Expected KHI=38, got $khiStockGrn"
    }
    Write-Host "[PASS] Stock balance verified after GRN outbox processing: KHI = $khiStockGrn EA."

    # Verify Trial Balance after Automated GRN Double-Entry Journal Posting
    $tbFinal = Invoke-RestMethod -Uri "$apiUrl/api/v1/finance/trial-balance" -Method Get
    $tbFinalDebit = ($tbFinal | Measure-Object -Property totalDebit -Sum).Sum
    $tbFinalCredit = ($tbFinal | Measure-Object -Property totalCredit -Sum).Sum
    if ([Math]::Abs($tbFinalDebit - $tbFinalCredit) -gt 0.001) {
        throw "Trial Balance is OUT OF BALANCE after GRN posting: Debit = $tbFinalDebit, Credit = $tbFinalCredit"
    }
    Write-Host "[PASS] Phase 3 Trial Balance verified mathematically balanced: Debits = Rs $tbFinalDebit, Credits = Rs $tbFinalCredit"

    # 15. Phase 4: Sales & POS Integration
    # 15.1 Create Customer
    $custBody = @{
        customerCode = "CUST-METRO-01"
        name = "Metro Retail & Supermarkets"
        customerType = "wholesale"
        contactName = "Imran Khan"
        email = "procurement@metro.pk"
        phone = "+923009988776"
        creditLimit = 200000
        paymentTermsDays = 15
        currencyCode = "PKR"
    } | ConvertTo-Json
    $cust = Invoke-RestMethod -Uri "$apiUrl/api/v1/sales/customers" -Method Post -Headers @{"Content-Type"="application/json"} -Body $custBody
    Write-Host "[PASS] Created customer $($cust.customerCode) ($($cust.name)) with Credit Limit Rs $($cust.creditLimit)"

    # Verify customer list contains CUST-METRO-01 and CUST-WALKIN
    $custList = Invoke-RestMethod -Uri "$apiUrl/api/v1/sales/customers" -Method Get
    $foundMetro = $custList | Where-Object { $_.customerCode -eq "CUST-METRO-01" }
    if (-not $foundMetro) {
        throw "Customer CUST-METRO-01 not found in customer list"
    }
    Write-Host "[PASS] Customer directory verified with $($custList.Count) customers."

    # 15.2 Create and Confirm Sales Order
    $soBody = @{
        orderNumber = "SO-2026-P4-001"
        customerId = $cust.customerId
        warehouseId = $wh1.warehouseId
        orderDate = (Get-Date).ToString("yyyy-MM-dd")
        note = "Bulk order for Metro superstore"
        lines = @(
            @{
                variantId = $variant.variantId
                binId = $bin1.binId
                orderedQty = 10
                unitOfMeasureId = $uomId
                unitPrice = 1500
                taxRate = 0
            }
        )
    } | ConvertTo-Json -Depth 5
    $so = Invoke-RestMethod -Uri "$apiUrl/api/v1/sales/orders" -Method Post -Headers @{"Content-Type"="application/json"} -Body $soBody
    if ($so.orderStatus -ne "draft" -or $so.totalAmount -ne 15000) {
        throw "Sales order creation failed: Status=$($so.orderStatus), Total=$($so.totalAmount)"
    }
    Write-Host "[PASS] Created draft Sales Order $($so.orderNumber) (Total: Rs $($so.totalAmount))."

    $soConfirmed = Invoke-RestMethod -Uri "$apiUrl/api/v1/sales/orders/$($so.orderId)/confirm" -Method Post -Headers @{"Content-Type"="application/json"}
    if ($soConfirmed.orderStatus -ne "confirmed") {
        throw "Sales order confirmation failed: Status=$($soConfirmed.orderStatus)"
    }
    Write-Host "[PASS] Confirmed Sales Order $($soConfirmed.orderNumber), status is $($soConfirmed.orderStatus)."

    # 15.3 POS Cash Sale with Tax, Inventory Relief & GL Integration
    $cashTxId = [Guid]::NewGuid().ToString()
    $cashInvBody = @{
        clientTransactionId = $cashTxId
        invoiceNumber = "INV-2026-CASH-001"
        customerId = $cust.customerId
        paymentMethod = "cash"
        invoiceDate = (Get-Date).ToString("yyyy-MM-dd")
        discountAmount = 0
        note = "Counter sale retail"
        lines = @(
            @{
                variantId = $variant.variantId
                warehouseId = $wh1.warehouseId
                binId = $bin1.binId
                quantity = 5
                unitOfMeasureId = $uomId
                unitPrice = 1500
                unitCost = 1200
                taxRate = 0.18
            }
        )
    } | ConvertTo-Json -Depth 5

    $cashInv = Invoke-RestMethod -Uri "$apiUrl/api/v1/sales/invoices" -Method Post -Headers @{"Content-Type"="application/json"; "Idempotency-Key"=$cashTxId} -Body $cashInvBody
    if ($cashInv.invoiceStatus -ne "posted" -or $cashInv.totalAmount -ne 8850 -or $cashInv.paidAmount -ne 8850) {
        throw "Cash invoice posting mismatch: Status=$($cashInv.invoiceStatus), Total=$($cashInv.totalAmount), Paid=$($cashInv.paidAmount)"
    }
    Write-Host "[PASS] Posted Cash POS Invoice $($cashInv.invoiceNumber) (Total: Rs $($cashInv.totalAmount), Paid: Rs $($cashInv.paidAmount))."

    # Verify Stock availability after Cash POS sale (decremented from 38 to 33)
    $availAfterCash = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/availability?variantId=$($variant.variantId)" -Method Get
    $khiStockCash = ($availAfterCash | Where-Object { $_.binId -eq $bin1.binId }).onHandQuantity
    if ($khiStockCash -ne 33) {
        throw "Stock after cash sale mismatch: Expected KHI=33, got $khiStockCash"
    }
    Write-Host "[PASS] Stock balance verified after Cash POS sale: KHI = $khiStockCash EA (relieved 5 EA)."

    # 15.4 Credit Sale with AR & Customer Ledger Balance Update
    $credTxId = [Guid]::NewGuid().ToString()
    $credInvBody = @{
        clientTransactionId = $credTxId
        invoiceNumber = "INV-2026-CRED-001"
        customerId = $cust.customerId
        paymentMethod = "credit"
        invoiceDate = (Get-Date).ToString("yyyy-MM-dd")
        discountAmount = 0
        note = "Metro Wholesale delivery"
        lines = @(
            @{
                variantId = $variant.variantId
                warehouseId = $wh1.warehouseId
                binId = $bin1.binId
                quantity = 3
                unitOfMeasureId = $uomId
                unitPrice = 1600
                unitCost = 1200
                taxRate = 0
            }
        )
    } | ConvertTo-Json -Depth 5

    $credInv = Invoke-RestMethod -Uri "$apiUrl/api/v1/sales/invoices" -Method Post -Headers @{"Content-Type"="application/json"} -Body $credInvBody
    if ($credInv.invoiceStatus -ne "posted" -or $credInv.totalAmount -ne 4800 -or $credInv.paidAmount -ne 0) {
        throw "Credit invoice posting mismatch: Status=$($credInv.invoiceStatus), Total=$($credInv.totalAmount), Paid=$($credInv.paidAmount)"
    }
    Write-Host "[PASS] Posted Credit Invoice $($credInv.invoiceNumber) (Total: Rs $($credInv.totalAmount), Paid: Rs $($credInv.paidAmount))."

    # Verify Customer Outstanding Balance increased to Rs 4,800
    $custUpdated = Invoke-RestMethod -Uri "$apiUrl/api/v1/sales/customers/$($cust.customerId)" -Method Get
    if ($custUpdated.outstandingBalance -ne 4800) {
        throw "Customer outstanding balance mismatch: Expected Rs 4,800, got Rs $($custUpdated.outstandingBalance)"
    }
    Write-Host "[PASS] Customer outstanding balance updated: Rs $($custUpdated.outstandingBalance) on credit."

    # Verify Stock availability after Credit sale (decremented from 33 to 30)
    $availAfterCred = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/availability?variantId=$($variant.variantId)" -Method Get
    $khiStockCred = ($availAfterCred | Where-Object { $_.binId -eq $bin1.binId }).onHandQuantity
    if ($khiStockCred -ne 30) {
        throw "Stock after credit sale mismatch: Expected KHI=30, got $khiStockCred"
    }
    Write-Host "[PASS] Stock balance verified after Credit sale: KHI = $khiStockCred EA (relieved 3 EA)."

    # 15.5 Insufficient Stock Sale Rejection
    $insTxId = [Guid]::NewGuid().ToString()
    $insInvBody = @{
        clientTransactionId = $insTxId
        invoiceNumber = "INV-2026-FAIL-001"
        customerId = $cust.customerId
        paymentMethod = "cash"
        lines = @(
            @{
                variantId = $variant.variantId
                warehouseId = $wh1.warehouseId
                binId = $bin1.binId
                quantity = 100
                unitOfMeasureId = $uomId
                unitPrice = 1500
            }
        )
    } | ConvertTo-Json -Depth 5

    $stockInsuffPassed = $false
    try {
        Invoke-RestMethod -Uri "$apiUrl/api/v1/sales/invoices" -Method Post -Headers @{"Content-Type"="application/json"} -Body $insInvBody
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 422) {
            $stockInsuffPassed = $true
        }
    }
    if (-not $stockInsuffPassed) {
        throw "Expected 422 Unprocessable Entity when requesting more stock than available."
    }
    Write-Host "[PASS] Insufficient stock check correctly rejected with 422 Unprocessable Entity."

    # 15.6 Idempotent Invoice Replay
    $cashReplay = Invoke-RestMethod -Uri "$apiUrl/api/v1/sales/invoices" -Method Post -Headers @{"Content-Type"="application/json"; "Idempotency-Key"=$cashTxId} -Body $cashInvBody
    if ($cashReplay.invoiceId -ne $cashInv.invoiceId) {
        throw "Idempotency replay returned different invoiceId: $($cashReplay.invoiceId)"
    }
    $availAfterReplay = Invoke-RestMethod -Uri "$apiUrl/api/v1/inventory/availability?variantId=$($variant.variantId)" -Method Get
    $khiStockReplay = ($availAfterReplay | Where-Object { $_.binId -eq $bin1.binId }).onHandQuantity
    if ($khiStockReplay -ne 30) {
        throw "Stock was mutated during idempotency replay! Expected 30, got $khiStockReplay"
    }
    Write-Host "[PASS] Idempotent invoice replay succeeded without double stock deduction (KHI = $khiStockReplay EA)."

    # 15.7 Final Trial Balance Verification after Sales & POS GL Journals
    $tbSales = Invoke-RestMethod -Uri "$apiUrl/api/v1/finance/trial-balance" -Method Get
    $tbSalesDebit = ($tbSales | Measure-Object -Property totalDebit -Sum).Sum
    $tbSalesCredit = ($tbSales | Measure-Object -Property totalCredit -Sum).Sum
    if ([Math]::Abs($tbSalesDebit - $tbSalesCredit) -gt 0.001) {
        throw "Trial Balance is OUT OF BALANCE after sales posting: Debit = $tbSalesDebit, Credit = $tbSalesCredit"
    }
    Write-Host "[PASS] Sales GL Journals posted & verified: Final Trial Balance balanced at Debits = Rs $tbSalesDebit, Credits = Rs $tbSalesCredit"

    Write-Host "`n--- Live Offline Synchronization Hardening Verification ---"

    # 1. Register device for offline receipt sync
    $clientDeviceId = [Guid]::NewGuid().ToString()
    $deviceRegBody = @{
        clientDeviceId = $clientDeviceId
        platformCode = "web"
        deviceName = "Automated Test Browser"
    } | ConvertTo-Json

    $deviceGrant = Invoke-RestMethod -Uri "$apiUrl/api/v1/sync/devices" -Method Post -Headers $headers -Body $deviceRegBody
    if (-not $deviceGrant.deviceId -or -not $deviceGrant.grantToken) {
        throw "Device registration failed to return valid deviceId or grantToken."
    }
    Write-Host "[PASS] Live device registration succeeded: DeviceId = $($deviceGrant.deviceId), GrantId = $($deviceGrant.grantId)"

    # 2. Upload an offline receipt batch
    $syncOpId = [Guid]::NewGuid().ToString()
    $syncBatchId = [Guid]::NewGuid().ToString()
    $syncBatchBody = @{
        batchId = $syncBatchId
        deviceId = $deviceGrant.deviceId
        grantId = $deviceGrant.grantId
        grantToken = $deviceGrant.grantToken
        operations = @(
            @{
                operationId = $syncOpId
                deviceSequence = 1
                operationType = "inventory.receipt.post"
                createdAt = (Get-Date).ToUniversalTime().ToString("o")
                payload = @{
                    variantId = $variant.variantId
                    warehouseId = $wh1.warehouseId
                    binId = $bin1.binId
                    quantity = 5
                    unitOfMeasureId = $uomId
                    note = "Offline sync test receipt"
                }
            }
        )
    } | ConvertTo-Json -Depth 5

    $syncBatchResp = Invoke-RestMethod -Uri "$apiUrl/api/v1/sync/receipts:batch" -Method Post -Headers $headers -Body $syncBatchBody
    $syncResult = $syncBatchResp.results | Where-Object { $_.operationId -eq $syncOpId }
    if ($syncResult.status -ne "accepted") {
        throw "Offline receipt batch failed: expected status 'accepted', got '$($syncResult.status)' ($($syncResult.code))"
    }
    Write-Host "[PASS] Offline receipt batch posted and accepted: MovementId = $($syncResult.receipt.movementId)"

    # 3. Test Lost Acknowledgement: Replay same batch with identical operationId
    $syncReplayResp = Invoke-RestMethod -Uri "$apiUrl/api/v1/sync/receipts:batch" -Method Post -Headers $headers -Body $syncBatchBody
    $syncReplayResult = $syncReplayResp.results | Where-Object { $_.operationId -eq $syncOpId }
    if ($syncReplayResult.status -ne "duplicate" -or $syncReplayResult.receipt.movementId -ne $syncResult.receipt.movementId) {
        throw "Idempotent batch replay failed: expected 'duplicate' with identical movementId, got '$($syncReplayResult.status)'"
    }
    Write-Host "[PASS] Lost-acknowledgement replay correctly returned 'duplicate' receipt without re-processing."

    # 4. Test Operation ID reuse conflict: Re-send same operationId with different quantity
    $syncConflictBatch = @{
        batchId = [Guid]::NewGuid().ToString()
        deviceId = $deviceGrant.deviceId
        grantId = $deviceGrant.grantId
        grantToken = $deviceGrant.grantToken
        operations = @(
            @{
                operationId = $syncOpId
                deviceSequence = 1
                operationType = "inventory.receipt.post"
                createdAt = (Get-Date).ToUniversalTime().ToString("o")
                payload = @{
                    variantId = $variant.variantId
                    warehouseId = $wh1.warehouseId
                    binId = $bin1.binId
                    quantity = 999
                    unitOfMeasureId = $uomId
                    note = "Tampered payload with reused ID"
                }
            }
        )
    } | ConvertTo-Json -Depth 5

    $syncConflictResp = Invoke-RestMethod -Uri "$apiUrl/api/v1/sync/receipts:batch" -Method Post -Headers $headers -Body $syncConflictBatch
    $syncConflictResult = $syncConflictResp.results | Where-Object { $_.operationId -eq $syncOpId }
    if ($syncConflictResult.status -ne "conflict" -or $syncConflictResult.code -ne "OPERATION_ID_REUSED") {
        throw "Expected OPERATION_ID_REUSED conflict, got status '$($syncConflictResult.status)' code '$($syncConflictResult.code)'"
    }
    Write-Host "[PASS] Operation ID reuse conflict correctly detected: status = 'conflict', code = 'OPERATION_ID_REUSED'"

    # 5. Verify incremental change feed: Read changes/inventory
    $changesResp = Invoke-RestMethod -Uri "$apiUrl/api/v1/sync/changes/inventory?after=0&limit=100" -Method Get -Headers $headers
    if ($changesResp.changes.Count -lt 1) {
        throw "Expected at least 1 inventory movement in change feed, got $($changesResp.changes.Count)"
    }
    Write-Host "[PASS] Incremental change feed verified: $($changesResp.changes.Count) events, next cursor = $($changesResp.nextCursor)"

    Write-Host "`n>>> ALL VERIFICATION CHECKS (PHASES 1, 2, 3, 4 & OFFLINE SYNC) PASSED SUCCESSFULLY! <<<`n"
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        Write-Host "Stopped API process."
    }
    # Clean up test tenant using a single transaction script
    $cleanupSql = @"
DO `$do`$
DECLARE
  v_tenant_id uuid;
BEGIN
  SELECT id INTO v_tenant_id FROM platform.tenants WHERE tenant_code = 'test-corp';
  IF v_tenant_id IS NOT NULL THEN
    DELETE FROM audit.audit_events WHERE tenant_id = v_tenant_id;
    DELETE FROM platform.tenant_provisioning_jobs WHERE tenant_id = v_tenant_id;
    DELETE FROM platform.tenant_entitlements WHERE tenant_id = v_tenant_id;
    DELETE FROM platform.subscriptions WHERE tenant_id = v_tenant_id;
    EXECUTE 'DELETE FROM "authorization".membership_role_assignments WHERE tenant_id = ' || quote_literal(v_tenant_id);
    EXECUTE 'DELETE FROM "authorization".roles WHERE tenant_id = ' || quote_literal(v_tenant_id);
    DELETE FROM identity.company_memberships WHERE tenant_id = v_tenant_id;
    DELETE FROM identity.tenant_memberships WHERE tenant_id = v_tenant_id;
    DELETE FROM organization.locations WHERE tenant_id = v_tenant_id;
    DELETE FROM organization.companies WHERE tenant_id = v_tenant_id;
    DELETE FROM platform.tenants WHERE id = v_tenant_id;
  END IF;
  DELETE FROM identity.users WHERE email_normalized = 'admin@test-corp.pk';

  -- Clean up Phase 4 demo records
  DELETE FROM erp_sales.sales_invoice_line WHERE invoice_id IN (SELECT invoice_id FROM erp_sales.sales_invoice WHERE invoice_number IN ('INV-2026-CASH-001', 'INV-2026-CRED-001'));
  DELETE FROM erp_sales.outbox_event WHERE invoice_id IN (SELECT invoice_id FROM erp_sales.sales_invoice WHERE invoice_number IN ('INV-2026-CASH-001', 'INV-2026-CRED-001'));
  DELETE FROM erp_sales.sales_invoice WHERE invoice_number IN ('INV-2026-CASH-001', 'INV-2026-CRED-001');
  DELETE FROM erp_sales.sales_order_line WHERE order_id IN (SELECT order_id FROM erp_sales.sales_order WHERE order_number = 'SO-2026-P4-001');
  DELETE FROM erp_sales.sales_order WHERE order_number = 'SO-2026-P4-001';
  DELETE FROM erp_sales.customer WHERE customer_code = 'CUST-METRO-01';

  -- Clean up Phase 3 demo records
  DELETE FROM erp_purchasing.goods_received_note_line WHERE grn_id IN (SELECT grn_id FROM erp_purchasing.goods_received_note WHERE grn_number = 'GRN-2026-P3-001');
  DELETE FROM erp_purchasing.outbox_event WHERE grn_id IN (SELECT grn_id FROM erp_purchasing.goods_received_note WHERE grn_number = 'GRN-2026-P3-001');
  DELETE FROM erp_purchasing.goods_received_note WHERE grn_number = 'GRN-2026-P3-001';
  DELETE FROM erp_purchasing.purchase_order_line WHERE purchase_order_id IN (SELECT purchase_order_id FROM erp_purchasing.purchase_order WHERE po_number = 'PO-2026-P3-001');
  DELETE FROM erp_purchasing.purchase_order WHERE po_number = 'PO-2026-P3-001';
  DELETE FROM erp_purchasing.supplier WHERE supplier_code = 'SUP-ENGRO-P3';

  UPDATE finance.journals SET status = 'draft' WHERE source_id IN ('POS-CLOSE-01') OR source_type IN ('grn', 'sales_invoice');
  DELETE FROM finance.journal_lines WHERE journal_id IN (SELECT id FROM finance.journals WHERE source_id IN ('POS-CLOSE-01') OR source_type IN ('grn', 'sales_invoice'));
  DELETE FROM finance.journals WHERE source_id IN ('POS-CLOSE-01') OR source_type IN ('grn', 'sales_invoice');

  DELETE FROM erp_inventory.inventory_movement_leg WHERE warehouse_id IN (SELECT warehouse_id FROM erp_inventory.warehouse WHERE code IN ('WH-KHI-01', 'WH-LHR-01'));
  DELETE FROM erp_inventory.inventory_movement WHERE note LIKE '%Initial batch receipt%' OR note LIKE '%Transfer KHI%' OR note LIKE '%Damaged during handling%' OR note LIKE '%GRN-2026-P3-001%' OR note LIKE '%INV-2026-%';
  DELETE FROM erp_inventory.stock_balance WHERE warehouse_id IN (SELECT warehouse_id FROM erp_inventory.warehouse WHERE code IN ('WH-KHI-01', 'WH-LHR-01'));
  DELETE FROM erp_inventory.stock_bin WHERE warehouse_id IN (SELECT warehouse_id FROM erp_inventory.warehouse WHERE code IN ('WH-KHI-01', 'WH-LHR-01'));
  DELETE FROM erp_inventory.warehouse WHERE code IN ('WH-KHI-01', 'WH-LHR-01');
  DELETE FROM erp_inventory.product_variant WHERE sku = 'SKU-RICE-5KG';
  DELETE FROM erp_inventory.product WHERE product_code = 'PROD-TEST-P3';

  -- Clean up sync records
  DELETE FROM sync.operation_conflicts WHERE device_id IN (SELECT id FROM identity.devices WHERE platform_code = 'web');
  DELETE FROM sync.operation_inbox WHERE aggregate_type = 'inventory_movement';
  DELETE FROM sync.offline_grants WHERE allowed_operations ? 'inventory.receipt.post';
  DELETE FROM sync.device_location_scopes WHERE device_id IN (SELECT id FROM identity.devices WHERE platform_code = 'web');
  DELETE FROM sync.device_company_scopes WHERE device_id IN (SELECT id FROM identity.devices WHERE platform_code = 'web');
  DELETE FROM identity.devices WHERE platform_code = 'web';
END `$do`$;
"@
    $cleanupFile = Join-Path $env:TEMP "cleanup-test-tenant.sql"
    [System.IO.File]::WriteAllText($cleanupFile, $cleanupSql)
    & docker cp $cleanupFile "universal-erp-postgres:/tmp/cleanup.sql"
    & docker exec universal-erp-postgres psql -U erp_dev -d universal_erp -f /tmp/cleanup.sql > $null 2>&1
    Remove-Item $cleanupFile -ErrorAction SilentlyContinue
    Write-Host "Cleaned up verification test tenant."
}
