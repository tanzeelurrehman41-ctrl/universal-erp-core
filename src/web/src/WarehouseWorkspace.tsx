import { useState, useEffect, useRef, type FormEvent } from "react";
import { apiFetch, type TenantCompanyContext } from "./auth";
import {
  parseGs1Barcode,
  formatGs1Barcode,
  parsePrefixedBarcode,
  sequencePickPath,
  calculateReplenishment,
  calculateBinUtilization,
  formatBinLocation,
  type ParsedGs1,
  type PickItem,
} from "./warehouseHelpers";

export type WarehouseZone = {
  id: string;
  zoneCode: string;
  name: string;
  zoneType: string;
  isPickFace: boolean;
  requiresColdChain: boolean;
  isHazardous: boolean;
  description?: string | null;
  isActive: boolean;
};

export type WarehouseBin = {
  id: string;
  warehouseId: string;
  zoneId?: string | null;
  zoneCode?: string | null;
  zoneName?: string | null;
  binCode: string;
  aisle?: string | null;
  rack?: string | null;
  shelf?: string | null;
  position?: string | null;
  pickSequence: number;
  maxWeightKg?: number | null;
  maxVolumeM3?: number | null;
  maxUnits?: number | null;
  currentUnits?: number;
  isPickFace: boolean;
  isColdChain: boolean;
  isHazardous: boolean;
  status: string;
};

export type BarcodeDefinition = {
  id: string;
  barcodeValue: string;
  barcodeFormat: string;
  entityType: string;
  entityId: string;
  gtin?: string | null;
  lotNumber?: string | null;
  expiryDate?: string | null;
  serialNumber?: string | null;
};

export type ScanAuditLog = {
  id: string;
  scannedAt: string;
  scannedBarcode: string;
  resolvedEntityType?: string | null;
  resolvedEntityId?: string | null;
  scanStatus: string;
  scanAction?: string | null;
  errorMessage?: string | null;
};

export type PutawayCandidate = {
  binId: string;
  binCode: string;
  zoneCode: string;
  zoneName: string;
  score: number;
  isPickFace: boolean;
  isColdChain: boolean;
  isHazardous: boolean;
  maxUnits?: number | null;
  currentUnits: number;
  availableCapacity: number;
  reasons: string[];
};

export type PickingWaveLine = {
  id: string;
  waveId: string;
  orderId?: string | null;
  orderLineId?: string | null;
  productVariantId: string;
  productName: string;
  sku: string;
  binId: string;
  binCode: string;
  zoneCode: string;
  aisle?: string;
  rack?: string;
  shelf?: string;
  position?: string;
  pickSequence: number;
  quantityRequired: number;
  quantityPicked: number;
  status: string;
};

export type PickingWave = {
  id: string;
  waveNumber: string;
  waveType: string;
  status: string;
  totalLines: number;
  pickedLines: number;
  createdAt: string;
  lines?: PickingWaveLine[];
};

export type WarehousePackage = {
  id: string;
  packageNumber: string;
  orderId?: string | null;
  waveId?: string | null;
  packageType: string;
  status: string;
  tareWeightKg: number;
  grossWeightKg?: number | null;
  carrierName?: string | null;
  trackingNumber?: string | null;
  shippedAt?: string | null;
  createdAt: string;
};

export type ReplenishmentRule = {
  id: string;
  pickBinId: string;
  pickBinCode?: string;
  reserveZoneId?: string | null;
  reserveZoneCode?: string;
  productVariantId: string;
  minQuantity: number;
  maxQuantity: number;
  reorderPoint: number;
  replenishmentIncrement: number;
  isActive: boolean;
};

export type ReplenishmentRecommendation = {
  ruleId: string;
  pickBinId: string;
  pickBinCode: string;
  productVariantId: string;
  currentPickStock: number;
  reorderPoint: number;
  maxQuantity: number;
  recommendedQuantity: number;
  reserveBinId?: string | null;
  reserveBinCode?: string | null;
  reserveAvailableStock: number;
};

export type QuarantineRecord = {
  id: string;
  binId: string;
  binCode?: string;
  productVariantId: string;
  quantity: number;
  quarantineReason: string;
  status: string;
  lotNumber?: string | null;
  quarantineBinId?: string | null;
  disposition?: string | null;
  dispositionNotes?: string | null;
  disposedAt?: string | null;
  createdAt: string;
};

type Tab =
  | "zones-bins"
  | "putaway"
  | "scanning"
  | "waves"
  | "replenishment"
  | "transfers"
  | "dispatch"
  | "quarantine"
  | "metrics";

type Props = {
  enabled: boolean;
  workspace: TenantCompanyContext | null;
};

export default function WarehouseWorkspace({ enabled, workspace }: Props) {
  const [activeTab, setActiveTab] = useState<Tab>("zones-bins");
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  // Master Data State
  const [zones, setZones] = useState<WarehouseZone[]>([]);
  const [bins, setBins] = useState<WarehouseBin[]>([]);
  const [waves, setWaves] = useState<PickingWave[]>([]);
  const [packages, setPackages] = useState<WarehousePackage[]>([]);
  const [replenishmentRules, setReplenishmentRules] = useState<ReplenishmentRule[]>([]);
  const [quarantineRecords, setQuarantineRecords] = useState<QuarantineRecord[]>([]);
  const [recentScans, setRecentScans] = useState<ScanAuditLog[]>([]);

  // Forms: Zone
  const [zoneCode, setZoneCode] = useState("");
  const [zoneName, setZoneName] = useState("");
  const [zoneType, setZoneType] = useState("ambient");
  const [zonePickFace, setZonePickFace] = useState(false);
  const [zoneColdChain, setZoneColdChain] = useState(false);
  const [zoneHazardous, setZoneHazardous] = useState(false);

  // Forms: Bin
  const [binCode, setBinCode] = useState("");
  const [binZoneId, setBinZoneId] = useState("");
  const [binAisle, setBinAisle] = useState("01");
  const [binRack, setBinRack] = useState("01");
  const [binShelf, setBinShelf] = useState("01");
  const [binPosition, setBinPosition] = useState("01");
  const [binMaxUnits, setBinMaxUnits] = useState("100");
  const [binMaxWeight, setBinMaxWeight] = useState("500");
  const [binIsPickFace, setBinIsPickFace] = useState(false);
  const [binIsColdChain, setBinIsColdChain] = useState(false);
  const [binIsHazardous, setBinIsHazardous] = useState(false);

  // Forms: Directed Putaway
  const [putawayVariantId, setPutawayVariantId] = useState("");
  const [putawayQty, setPutawayQty] = useState("10");
  const [putawayColdChain, setPutawayColdChain] = useState(false);
  const [putawayHazardous, setPutawayHazardous] = useState(false);
  const [putawayCandidates, setPutawayCandidates] = useState<PutawayCandidate[]>([]);
  const [selectedPutawayBinId, setSelectedPutawayBinId] = useState("");
  const [putawaySourceBinId, setPutawaySourceBinId] = useState("");

  // Forms: Scanner
  const [barcodeInput, setBarcodeInput] = useState("");
  const [parsedGs1, setParsedGs1] = useState<ParsedGs1 | null>(null);
  const [prefixedInfo, setPrefixedInfo] = useState<{ type: string; code: string; isPrefixed: boolean } | null>(null);
  const lastScanTimestamp = useRef<number>(0);
  const lastScanBarcode = useRef<string>("");

  // Forms: Barcode Registration
  const [regBarcodeValue, setRegBarcodeValue] = useState("");
  const [regEntityType, setRegEntityType] = useState("product");
  const [regEntityId, setRegEntityId] = useState("");
  const [regGtin, setRegGtin] = useState("");
  const [regLot, setRegLot] = useState("");
  const [regExpiry, setRegExpiry] = useState("");
  const [regSerial, setRegSerial] = useState("");

  // Forms: Wave & Picking
  const [waveNumber, setWaveNumber] = useState(`WAVE-${new Date().getFullYear()}-${Math.floor(Math.random() * 900 + 100)}`);
  const [waveType, setWaveType] = useState("batch");
  const [selectedWaveId, setSelectedWaveId] = useState("");
  const [waveLineVariantId, setWaveLineVariantId] = useState("");
  const [waveLineProductName, setWaveLineProductName] = useState("");
  const [waveLineSku, setWaveLineSku] = useState("");
  const [waveLineBinId, setWaveLineBinId] = useState("");
  const [waveLineQty, setWaveLineQty] = useState("5");
  const [pickConfirmLineId, setPickConfirmLineId] = useState("");
  const [pickConfirmBarcode, setPickConfirmBarcode] = useState("");
  const [pickConfirmQty, setPickConfirmQty] = useState("1");

  // Forms: Replenishment
  const [replenPickBinId, setReplenPickBinId] = useState("");
  const [replenReserveZoneId, setReplenReserveZoneId] = useState("");
  const [replenVariantId, setReplenVariantId] = useState("");
  const [replenMin, setReplenMin] = useState("10");
  const [replenMax, setReplenMax] = useState("50");
  const [replenReorder, setReplenReorder] = useState("15");
  const [replenIncrement, setReplenIncrement] = useState("5");
  const [replenRecommendations, setReplenRecommendations] = useState<ReplenishmentRecommendation[]>([]);

  // Forms: Bin Transfer
  const [transferSourceBinId, setTransferSourceBinId] = useState("");
  const [transferTargetBinId, setTransferTargetBinId] = useState("");
  const [transferVariantId, setTransferVariantId] = useState("");
  const [transferQty, setTransferQty] = useState("1");
  const [transferReason, setTransferReason] = useState("reorganization");

  // Forms: Packages & Dispatch
  const [pkgNumber, setPkgNumber] = useState(`PKG-${new Date().getFullYear()}-${Math.floor(Math.random() * 9000 + 1000)}`);
  const [pkgType, setPkgType] = useState("carton");
  const [pkgTareWeight, setPkgTareWeight] = useState("0.5");
  const [selectedPkgId, setSelectedPkgId] = useState("");
  const [sealGrossWeight, setSealGrossWeight] = useState("12.5");
  const [dispatchCarrier, setDispatchCarrier] = useState("TCS Express");
  const [dispatchTracking, setDispatchTracking] = useState("");

  // Forms: Quarantine
  const [qBinId, setQBinId] = useState("");
  const [qVariantId, setQVariantId] = useState("");
  const [qQty, setQQty] = useState("5");
  const [qReason, setQReason] = useState("damaged");
  const [qLotNumber, setQLotNumber] = useState("");
  const [qQuarantineBinId, setQQuarantineBinId] = useState("");
  const [selectedQId, setSelectedQId] = useState("");
  const [qDisposition, setQDisposition] = useState("release_to_stock");
  const [qTargetBinId, setQTargetBinId] = useState("");
  const [qDispNotes, setQDispNotes] = useState("");

  // Load data
  const loadZonesAndBins = async () => {
    if (!enabled || !workspace) return;
    try {
      setLoading(true);
      const [zRes, bRes] = await Promise.all([
        apiFetch("/api/v1/warehouse/zones", {}, workspace),
        apiFetch("/api/v1/warehouse/bins", {}, workspace),
      ]);
      if (zRes.ok) setZones((await zRes.json()) as WarehouseZone[]);
      if (bRes.ok) setBins((await bRes.json()) as WarehouseBin[]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load warehouse master data");
    } finally {
      setLoading(false);
    }
  };

  const loadWaves = async () => {
    if (!enabled || !workspace) return;
    try {
      const res = await apiFetch("/api/v1/warehouse/waves", {}, workspace);
      if (res.ok) setWaves((await res.json()) as PickingWave[]);
    } catch {
      // Ignore
    }
  };

  const loadPackages = async () => {
    if (!enabled || !workspace) return;
    try {
      const res = await apiFetch("/api/v1/warehouse/packages", {}, workspace);
      if (res.ok) setPackages((await res.json()) as WarehousePackage[]);
    } catch {
      // Ignore
    }
  };

  const loadReplenishment = async () => {
    if (!enabled || !workspace) return;
    try {
      const res = await apiFetch("/api/v1/warehouse/replenishment-rules", {}, workspace);
      if (res.ok) setReplenishmentRules((await res.json()) as ReplenishmentRule[]);
    } catch {
      // Ignore
    }
  };

  const loadQuarantine = async () => {
    if (!enabled || !workspace) return;
    try {
      const res = await apiFetch("/api/v1/warehouse/quarantine", {}, workspace);
      if (res.ok) setQuarantineRecords((await res.json()) as QuarantineRecord[]);
    } catch {
      // Ignore
    }
  };

  const loadScans = async () => {
    if (!enabled || !workspace) return;
    try {
      const res = await apiFetch("/api/v1/warehouse/scans?limit=15", {}, workspace);
      if (res.ok) setRecentScans((await res.json()) as ScanAuditLog[]);
    } catch {
      // Ignore
    }
  };

  useEffect(() => {
    if (enabled && workspace) {
      void loadZonesAndBins();
      void loadWaves();
      void loadPackages();
      void loadReplenishment();
      void loadQuarantine();
      void loadScans();
    }
  }, [enabled, workspace]);

  // Zone Creation
  const handleCreateZone = async (e: FormEvent) => {
    e.preventDefault();
    if (!zoneCode || !zoneName) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/zones",
        {
          method: "POST",
          body: JSON.stringify({
            zoneCode: zoneCode.toUpperCase(),
            name: zoneName,
            zoneType,
            isPickFace: zonePickFace,
            requiresColdChain: zoneColdChain,
            isHazardous: zoneHazardous,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage(`Zone ${zoneCode.toUpperCase()} created successfully.`);
      setZoneCode("");
      setZoneName("");
      void loadZonesAndBins();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create zone");
    } finally {
      setLoading(false);
    }
  };

  // Bin Creation
  const handleCreateBin = async (e: FormEvent) => {
    e.preventDefault();
    if (!binCode) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/bins",
        {
          method: "POST",
          body: JSON.stringify({
            binCode: binCode.toUpperCase(),
            zoneId: binZoneId || null,
            aisle: binAisle,
            rack: binRack,
            shelf: binShelf,
            position: binPosition,
            maxUnits: parseInt(binMaxUnits, 10) || 100,
            maxWeightKg: parseFloat(binMaxWeight) || 500,
            isPickFace: binIsPickFace,
            isColdChain: binIsColdChain,
            isHazardous: binIsHazardous,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage(`Bin ${binCode.toUpperCase()} created successfully.`);
      setBinCode("");
      void loadZonesAndBins();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create bin");
    } finally {
      setLoading(false);
    }
  };

  // Directed Putaway Recommendation
  const handleGetPutawayRecommendations = async (e: FormEvent) => {
    e.preventDefault();
    if (!putawayVariantId) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/putaway/recommend",
        {
          method: "POST",
          body: JSON.stringify({
            productVariantId: putawayVariantId,
            quantity: parseFloat(putawayQty) || 1,
            requiresColdChain: putawayColdChain,
            isHazardous: putawayHazardous,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      const candidates = (await res.json()) as PutawayCandidate[];
      setPutawayCandidates(candidates);
      if (candidates.length > 0) {
        setSelectedPutawayBinId(candidates[0].binId);
        setMessage(`Found ${candidates.length} candidate putaway bins ranked by score.`);
      } else {
        setMessage("No suitable candidate bins found matching constraints.");
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Putaway query failed");
    } finally {
      setLoading(false);
    }
  };

  // Execute Putaway
  const handleExecutePutaway = async () => {
    if (!putawaySourceBinId || !selectedPutawayBinId || !putawayVariantId) {
      setError("Select a source dock bin, target candidate bin, and product.");
      return;
    }
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/putaway/execute",
        {
          method: "POST",
          body: JSON.stringify({
            sourceBinId: putawaySourceBinId,
            targetBinId: selectedPutawayBinId,
            productVariantId: putawayVariantId,
            quantity: parseFloat(putawayQty) || 1,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage("Putaway executed successfully! Stock transferred with Model A FIFO preservation.");
      setPutawayCandidates([]);
      void loadZonesAndBins();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Putaway execution failed");
    } finally {
      setLoading(false);
    }
  };

  // Fast Barcode Scanning with Debounce
  const handleBarcodeScan = async (e: FormEvent) => {
    e.preventDefault();
    const raw = barcodeInput.trim();
    if (!raw) return;

    // Debounce duplicate scans within 1500ms
    const now = Date.now();
    if (raw === lastScanBarcode.current && now - lastScanTimestamp.current < 1500) {
      setMessage(`Debounced duplicate scan of "${raw}".`);
      return;
    }
    lastScanTimestamp.current = now;
    lastScanBarcode.current = raw;

    // Parse locally for instant UI feedback
    const gs1 = parseGs1Barcode(raw);
    setParsedGs1(gs1);
    const pref = parsePrefixedBarcode(raw);
    setPrefixedInfo(pref);

    setError("");
    setMessage("");
    try {
      const res = await apiFetch(
        "/api/v1/warehouse/scan",
        {
          method: "POST",
          body: JSON.stringify({ barcode: raw }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`Scan resolution failed (HTTP ${res.status})`);
      const scanResult = await res.json();
      setMessage(`Scan resolved: [${scanResult.entityType}] ${scanResult.entityId}`);
      void loadScans();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Barcode scan error");
    }
  };

  // Register Barcode
  const handleRegisterBarcode = async (e: FormEvent) => {
    e.preventDefault();
    if (!regBarcodeValue || !regEntityId) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/barcodes",
        {
          method: "POST",
          body: JSON.stringify({
            barcodeValue: regBarcodeValue,
            barcodeFormat: regBarcodeValue.startsWith("(") ? "gs1_128" : "code128",
            entityType: regEntityType,
            entityId: regEntityId,
            gtin: regGtin || null,
            lotNumber: regLot || null,
            expiryDate: regExpiry ? new Date(regExpiry).toISOString() : null,
            serialNumber: regSerial || null,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage(`Barcode ${regBarcodeValue} registered to ${regEntityType}.`);
      setRegBarcodeValue("");
      setRegEntityId("");
      setRegGtin("");
      setRegLot("");
      setRegSerial("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Registration failed");
    } finally {
      setLoading(false);
    }
  };

  // Create Wave
  const handleCreateWave = async (e: FormEvent) => {
    e.preventDefault();
    if (!waveNumber) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/waves",
        {
          method: "POST",
          body: JSON.stringify({ waveNumber, waveType }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      const newWave = (await res.json()) as PickingWave;
      setMessage(`Wave ${newWave.waveNumber} created.`);
      setSelectedWaveId(newWave.id);
      void loadWaves();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Wave creation failed");
    } finally {
      setLoading(false);
    }
  };

  // Add Line to Selected Wave
  const handleAddWaveLine = async (e: FormEvent) => {
    e.preventDefault();
    if (!selectedWaveId || !waveLineVariantId || !waveLineBinId) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        `/api/v1/warehouse/waves/${selectedWaveId}/lines`,
        {
          method: "POST",
          body: JSON.stringify({
            lines: [
              {
                productVariantId: waveLineVariantId,
                productName: waveLineProductName || "Item",
                sku: waveLineSku || "SKU-001",
                binId: waveLineBinId,
                quantityRequired: parseFloat(waveLineQty) || 1,
              },
            ],
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage("Pick line added and sequenced into wave.");
      setWaveLineVariantId("");
      setWaveLineProductName("");
      setWaveLineSku("");
      void loadWaves();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to add pick line");
    } finally {
      setLoading(false);
    }
  };

  // Confirm Pick Line
  const handleConfirmPick = async (e: FormEvent) => {
    e.preventDefault();
    if (!selectedWaveId || !pickConfirmLineId || !pickConfirmBarcode) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/waves/pick-confirm",
        {
          method: "POST",
          body: JSON.stringify({
            waveId: selectedWaveId,
            waveLineId: pickConfirmLineId,
            scannedBarcode: pickConfirmBarcode,
            pickedQuantity: parseFloat(pickConfirmQty) || 1,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage("Pick confirmed via barcode verification.");
      setPickConfirmBarcode("");
      void loadWaves();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Pick confirmation failed");
    } finally {
      setLoading(false);
    }
  };

  // Replenishment Rule Creation
  const handleCreateReplenRule = async (e: FormEvent) => {
    e.preventDefault();
    if (!replenPickBinId || !replenVariantId) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/replenishment-rules",
        {
          method: "POST",
          body: JSON.stringify({
            pickBinId: replenPickBinId,
            reserveZoneId: replenReserveZoneId || null,
            productVariantId: replenVariantId,
            minQuantity: parseFloat(replenMin) || 10,
            maxQuantity: parseFloat(replenMax) || 50,
            reorderPoint: parseFloat(replenReorder) || 15,
            replenishmentIncrement: parseFloat(replenIncrement) || 5,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage("Replenishment rule registered.");
      void loadReplenishment();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to register replenishment rule");
    } finally {
      setLoading(false);
    }
  };

  // Generate Replenishment Recommendations
  const handleGenerateReplenishment = async () => {
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/replenishment/recommend",
        {
          method: "POST",
          body: JSON.stringify({}),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      const recs = (await res.json()) as ReplenishmentRecommendation[];
      setReplenRecommendations(recs);
      setMessage(`Generated ${recs.length} replenishment recommendations.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to generate replenishment recommendations");
    } finally {
      setLoading(false);
    }
  };

  // Execute Replenishment
  const handleExecuteReplenishment = async (rec: ReplenishmentRecommendation) => {
    if (!rec.reserveBinId) {
      setError("No reserve bin assigned for replenishment.");
      return;
    }
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/replenishment/execute",
        {
          method: "POST",
          body: JSON.stringify({
            ruleId: rec.ruleId,
            fromBinId: rec.reserveBinId,
            toBinId: rec.pickBinId,
            productVariantId: rec.productVariantId,
            quantity: rec.recommendedQuantity,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage(`Replenished ${rec.recommendedQuantity} units to pick bin ${rec.pickBinCode}.`);
      void handleGenerateReplenishment();
      void loadZonesAndBins();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Replenishment execution failed");
    } finally {
      setLoading(false);
    }
  };

  // Bin-to-Bin Transfer
  const handleBinTransfer = async (e: FormEvent) => {
    e.preventDefault();
    if (!transferSourceBinId || !transferTargetBinId || !transferVariantId) return;
    if (transferSourceBinId === transferTargetBinId) {
      setError("Source and destination bins cannot be identical.");
      return;
    }
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/transfers",
        {
          method: "POST",
          body: JSON.stringify({
            sourceBinId: transferSourceBinId,
            targetBinId: transferTargetBinId,
            productVariantId: transferVariantId,
            quantity: parseFloat(transferQty) || 1,
            reasonCode: transferReason,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage("Bin transfer executed successfully with atomic Model A FIFO preservation.");
      void loadZonesAndBins();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Bin transfer failed");
    } finally {
      setLoading(false);
    }
  };

  // Create Package
  const handleCreatePackage = async (e: FormEvent) => {
    e.preventDefault();
    if (!pkgNumber) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/packages",
        {
          method: "POST",
          body: JSON.stringify({
            packageNumber: pkgNumber,
            packageType: pkgType,
            tareWeightKg: parseFloat(pkgTareWeight) || 0.5,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage(`Package ${pkgNumber} created.`);
      setPkgNumber(`PKG-${new Date().getFullYear()}-${Math.floor(Math.random() * 9000 + 1000)}`);
      void loadPackages();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Package creation failed");
    } finally {
      setLoading(false);
    }
  };

  // Seal Package
  const handleSealPackage = async () => {
    if (!selectedPkgId) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        `/api/v1/warehouse/packages/${selectedPkgId}/seal`,
        {
          method: "POST",
          body: JSON.stringify({
            grossWeightKg: parseFloat(sealGrossWeight) || 1.0,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage("Package sealed and verified.");
      void loadPackages();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Seal failed");
    } finally {
      setLoading(false);
    }
  };

  // Dispatch Package
  const handleDispatchPackage = async () => {
    if (!selectedPkgId) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        `/api/v1/warehouse/packages/${selectedPkgId}/dispatch`,
        {
          method: "POST",
          body: JSON.stringify({
            carrierName: dispatchCarrier,
            trackingNumber: dispatchTracking || `TRK-${Date.now().toString().slice(-8)}`,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage("Package marked as Dispatched with carrier tracking.");
      void loadPackages();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Dispatch failed");
    } finally {
      setLoading(false);
    }
  };

  // Quarantine Hold Creation
  const handleCreateQuarantine = async (e: FormEvent) => {
    e.preventDefault();
    if (!qBinId || !qVariantId) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        "/api/v1/warehouse/quarantine",
        {
          method: "POST",
          body: JSON.stringify({
            binId: qBinId,
            productVariantId: qVariantId,
            quantity: parseFloat(qQty) || 1,
            quarantineReason: qReason,
            lotNumber: qLotNumber || null,
            quarantineBinId: qQuarantineBinId || null,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage("Stock routed to quarantine hold.");
      void loadQuarantine();
      void loadZonesAndBins();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Quarantine hold failed");
    } finally {
      setLoading(false);
    }
  };

  // Quarantine Disposition
  const handleDisposeQuarantine = async () => {
    if (!selectedQId) return;
    setError("");
    setMessage("");
    try {
      setLoading(true);
      const res = await apiFetch(
        `/api/v1/warehouse/quarantine/${selectedQId}/disposition`,
        {
          method: "POST",
          body: JSON.stringify({
            disposition: qDisposition,
            targetBinId: qTargetBinId || null,
            notes: qDispNotes || null,
          }),
        },
        workspace
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      setMessage(`Quarantine record disposed via ${qDisposition}.`);
      void loadQuarantine();
      void loadZonesAndBins();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Disposition failed");
    } finally {
      setLoading(false);
    }
  };

  const selectedWave = waves.find((w) => w.id === selectedWaveId);

  // Serpentine sequenced lines for active wave
  const activeWaveSequencedLines: PickItem[] = selectedWave?.lines
    ? sequencePickPath(
        selectedWave.lines.map((l) => ({
          id: l.id,
          variantId: l.productVariantId,
          productName: l.productName,
          sku: l.sku,
          binId: l.binId,
          binCode: l.binCode,
          zoneCode: l.zoneCode,
          aisle: l.aisle || "01",
          rack: l.rack || "01",
          shelf: l.shelf || "01",
          position: l.position || "01",
          quantityRequired: l.quantityRequired,
          quantityPicked: l.quantityPicked,
          status: l.status,
        }))
      )
    : [];

  return (
    <section className="catalog" aria-labelledby="warehouse-title">
      <div className="catalog-heading">
        <div>
          <p className="eyebrow">OPERATIONS · WAREHOUSE & BARCODE LOGISTICS</p>
          <h2 id="warehouse-title">Warehouse Management System (WMS)</h2>
        </div>
        <span className="tag tag-progress">
          {workspace?.companyName ?? "Warehouse Logistics"}
        </span>
      </div>

      <p className="catalog-note">
        High-throughput directed putaway, serpentine wave picking, GS1-128 barcode engine, min/max replenishment, cartonization and quarantine management strictly preserving Model A FIFO layers.
      </p>

      {message && (
        <div style={{ background: "#ecfdf5", color: "#065f46", padding: "10px 14px", borderRadius: "6px", marginBottom: "14px", border: "1px solid #a7f3d0" }}>
          {message}
        </div>
      )}

      {error && (
        <div style={{ background: "#fef2f2", color: "#991b1b", padding: "10px 14px", borderRadius: "6px", marginBottom: "14px", border: "1px solid #fecaca" }}>
          {error}
        </div>
      )}

      {/* Subtab Navigation */}
      <nav
        aria-label="Warehouse Operations"
        style={{
          display: "flex",
          flexWrap: "wrap",
          gap: "6px",
          marginBottom: "16px",
          padding: "6px",
          background: "#f1f5f9",
          borderRadius: "8px",
        }}
      >
        {[
          { id: "zones-bins", label: "🏷️ Zones & Bins" },
          { id: "putaway", label: "📥 Directed Putaway" },
          { id: "scanning", label: "⚡ Fast Barcode & GS1" },
          { id: "waves", label: "🌊 Picking Waves" },
          { id: "replenishment", label: "🔄 Replenishment" },
          { id: "transfers", label: "↔️ Bin Transfers" },
          { id: "dispatch", label: "📦 Packing & Dispatch" },
          { id: "quarantine", label: "⚠️ Quarantine & Returns" },
          { id: "metrics", label: "📊 WMS Metrics" },
        ].map((tab) => (
          <button
            key={tab.id}
            type="button"
            onClick={() => {
              setActiveTab(tab.id as Tab);
              setMessage("");
              setError("");
            }}
            style={{
              padding: "6px 12px",
              borderRadius: "5px",
              border: "1px solid",
              borderColor: activeTab === tab.id ? "#2563eb" : "#cbd5e1",
              background: activeTab === tab.id ? "#2563eb" : "#ffffff",
              color: activeTab === tab.id ? "#ffffff" : "#334155",
              fontWeight: activeTab === tab.id ? 600 : 500,
              fontSize: "12px",
              cursor: "pointer",
            }}
          >
            {tab.label}
          </button>
        ))}
      </nav>

      {/* VIEW 1: ZONES & BINS */}
      {activeTab === "zones-bins" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Add Warehouse Zone</h3>
            <form onSubmit={handleCreateZone} style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
              <label style={{ fontSize: "12px" }}>
                Zone Code:
                <input
                  type="text"
                  required
                  placeholder="e.g. ZONE-A"
                  value={zoneCode}
                  onChange={(e) => setZoneCode(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                />
              </label>
              <label style={{ fontSize: "12px" }}>
                Zone Name:
                <input
                  type="text"
                  required
                  placeholder="e.g. Fast-Moving Ambient Rack"
                  value={zoneName}
                  onChange={(e) => setZoneName(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                />
              </label>
              <label style={{ fontSize: "12px" }}>
                Zone Type:
                <select
                  value={zoneType}
                  onChange={(e) => setZoneType(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                >
                  <option value="ambient">Ambient Storage</option>
                  <option value="cold_chain">Cold Chain (2°C - 8°C)</option>
                  <option value="hazardous">Hazardous / Chemical</option>
                  <option value="receiving">Receiving Dock</option>
                  <option value="dispatch">Dispatch / Staging</option>
                  <option value="quarantine">Quarantine Inspection</option>
                </select>
              </label>
              <div style={{ display: "flex", gap: "16px", marginTop: "4px" }}>
                <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px" }}>
                  <input
                    type="checkbox"
                    checked={zonePickFace}
                    onChange={(e) => setZonePickFace(e.target.checked)}
                  />
                  Pick Face Zone
                </label>
                <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px" }}>
                  <input
                    type="checkbox"
                    checked={zoneColdChain}
                    onChange={(e) => setZoneColdChain(e.target.checked)}
                  />
                  Cold Chain
                </label>
                <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px" }}>
                  <input
                    type="checkbox"
                    checked={zoneHazardous}
                    onChange={(e) => setZoneHazardous(e.target.checked)}
                  />
                  Hazardous
                </label>
              </div>
              <button
                type="submit"
                disabled={loading}
                style={{ padding: "8px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "5px", cursor: "pointer", fontWeight: 600 }}
              >
                Create Zone
              </button>
            </form>

            <h4 style={{ marginTop: "20px", marginBottom: "8px", fontSize: "13px" }}>Active Zones ({zones.length})</h4>
            <div style={{ maxHeight: "200px", overflowY: "auto", border: "1px solid #e2e8f0", borderRadius: "6px" }}>
              <table style={{ width: "100%", fontSize: "11px", borderCollapse: "collapse" }}>
                <thead>
                  <tr style={{ background: "#f8fafc", textAlign: "left" }}>
                    <th style={{ padding: "6px" }}>Code</th>
                    <th style={{ padding: "6px" }}>Name</th>
                    <th style={{ padding: "6px" }}>Type</th>
                    <th style={{ padding: "6px" }}>Pick Face?</th>
                  </tr>
                </thead>
                <tbody>
                  {zones.map((z) => (
                    <tr key={z.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                      <td style={{ padding: "6px", fontWeight: 600 }}>{z.zoneCode}</td>
                      <td style={{ padding: "6px" }}>{z.name}</td>
                      <td style={{ padding: "6px" }}>{z.zoneType}</td>
                      <td style={{ padding: "6px" }}>{z.isPickFace ? "Yes" : "No"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Add Storage Bin</h3>
            <form onSubmit={handleCreateBin} style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <label style={{ fontSize: "12px" }}>
                  Bin Code:
                  <input
                    type="text"
                    required
                    placeholder="e.g. A-01-02-01"
                    value={binCode}
                    onChange={(e) => setBinCode(e.target.value)}
                    style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                  />
                </label>
                <label style={{ fontSize: "12px" }}>
                  Zone:
                  <select
                    value={binZoneId}
                    onChange={(e) => setBinZoneId(e.target.value)}
                    style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                  >
                    <option value="">No Zone Assigned</option>
                    {zones.map((z) => (
                      <option key={z.id} value={z.id}>
                        {z.zoneCode} ({z.name})
                      </option>
                    ))}
                  </select>
                </label>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr)", gap: "6px" }}>
                <label style={{ fontSize: "11px" }}>
                  Aisle:
                  <input
                    type="text"
                    value={binAisle}
                    onChange={(e) => setBinAisle(e.target.value)}
                    style={{ width: "100%", padding: "4px" }}
                  />
                </label>
                <label style={{ fontSize: "11px" }}>
                  Rack:
                  <input
                    type="text"
                    value={binRack}
                    onChange={(e) => setBinRack(e.target.value)}
                    style={{ width: "100%", padding: "4px" }}
                  />
                </label>
                <label style={{ fontSize: "11px" }}>
                  Shelf:
                  <input
                    type="text"
                    value={binShelf}
                    onChange={(e) => setBinShelf(e.target.value)}
                    style={{ width: "100%", padding: "4px" }}
                  />
                </label>
                <label style={{ fontSize: "11px" }}>
                  Pos:
                  <input
                    type="text"
                    value={binPosition}
                    onChange={(e) => setBinPosition(e.target.value)}
                    style={{ width: "100%", padding: "4px" }}
                  />
                </label>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <label style={{ fontSize: "12px" }}>
                  Max Units:
                  <input
                    type="number"
                    value={binMaxUnits}
                    onChange={(e) => setBinMaxUnits(e.target.value)}
                    style={{ width: "100%", padding: "6px" }}
                  />
                </label>
                <label style={{ fontSize: "12px" }}>
                  Max Weight (kg):
                  <input
                    type="number"
                    value={binMaxWeight}
                    onChange={(e) => setBinMaxWeight(e.target.value)}
                    style={{ width: "100%", padding: "6px" }}
                  />
                </label>
              </div>

              <div style={{ display: "flex", gap: "16px" }}>
                <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px" }}>
                  <input
                    type="checkbox"
                    checked={binIsPickFace}
                    onChange={(e) => setBinIsPickFace(e.target.checked)}
                  />
                  Pick Face Bin
                </label>
                <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px" }}>
                  <input
                    type="checkbox"
                    checked={binIsColdChain}
                    onChange={(e) => setBinIsColdChain(e.target.checked)}
                  />
                  Cold Chain
                </label>
                <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px" }}>
                  <input
                    type="checkbox"
                    checked={binIsHazardous}
                    onChange={(e) => setBinIsHazardous(e.target.checked)}
                  />
                  Hazardous
                </label>
              </div>

              <button
                type="submit"
                disabled={loading}
                style={{ padding: "8px", background: "#059669", color: "#fff", border: "none", borderRadius: "5px", cursor: "pointer", fontWeight: 600 }}
              >
                Create Bin
              </button>
            </form>

            <h4 style={{ marginTop: "20px", marginBottom: "8px", fontSize: "13px" }}>Bins List ({bins.length})</h4>
            <div style={{ maxHeight: "200px", overflowY: "auto", border: "1px solid #e2e8f0", borderRadius: "6px" }}>
              <table style={{ width: "100%", fontSize: "11px", borderCollapse: "collapse" }}>
                <thead>
                  <tr style={{ background: "#f8fafc", textAlign: "left" }}>
                    <th style={{ padding: "6px" }}>Code</th>
                    <th style={{ padding: "6px" }}>Zone</th>
                    <th style={{ padding: "6px" }}>Location</th>
                    <th style={{ padding: "6px" }}>Capacity</th>
                    <th style={{ padding: "6px" }}>Pick Face</th>
                  </tr>
                </thead>
                <tbody>
                  {bins.map((b) => (
                    <tr key={b.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                      <td style={{ padding: "6px", fontWeight: 600 }}>{b.binCode}</td>
                      <td style={{ padding: "6px" }}>{b.zoneCode || "-"}</td>
                      <td style={{ padding: "6px" }}>{formatBinLocation(b.zoneCode || "Z", b.aisle || "01", b.rack || "01", b.shelf || "01", b.position || "01")}</td>
                      <td style={{ padding: "6px" }}>{b.maxUnits ?? "∞"} u</td>
                      <td style={{ padding: "6px" }}>{b.isPickFace ? "⭐ Yes" : "No"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* VIEW 2: DIRECTED PUTAWAY */}
      {activeTab === "putaway" && (
        <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
          <h3 style={{ marginTop: 0, fontSize: "15px" }}>Directed Putaway Optimization</h3>
          <p style={{ fontSize: "12px", color: "#64748b" }}>
            Multi-factor scoring algorithm evaluates dedicated pick face affinity, cold-chain compatibility, hazardous segregation, and remaining volumetric capacity.
          </p>

          <form onSubmit={handleGetPutawayRecommendations} style={{ display: "grid", gridTemplateColumns: "repeat(4, 1fr) auto", gap: "10px", alignItems: "end" }}>
            <label style={{ fontSize: "12px" }}>
              Product Variant ID:
              <input
                type="text"
                required
                placeholder="UUID of variant"
                value={putawayVariantId}
                onChange={(e) => setPutawayVariantId(e.target.value)}
                style={{ width: "100%", padding: "6px", marginTop: "4px" }}
              />
            </label>
            <label style={{ fontSize: "12px" }}>
              Quantity Received:
              <input
                type="number"
                value={putawayQty}
                onChange={(e) => setPutawayQty(e.target.value)}
                style={{ width: "100%", padding: "6px", marginTop: "4px" }}
              />
            </label>
            <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px", paddingBottom: "8px" }}>
              <input
                type="checkbox"
                checked={putawayColdChain}
                onChange={(e) => setPutawayColdChain(e.target.checked)}
              />
              Requires Cold Chain
            </label>
            <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px", paddingBottom: "8px" }}>
              <input
                type="checkbox"
                checked={putawayHazardous}
                onChange={(e) => setPutawayHazardous(e.target.checked)}
              />
              Is Hazardous
            </label>
            <button
              type="submit"
              disabled={loading}
              style={{ padding: "8px 16px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "5px", cursor: "pointer", fontWeight: 600, height: "36px" }}
            >
              Find Candidate Bins
            </button>
          </form>

          {putawayCandidates.length > 0 && (
            <div style={{ marginTop: "20px" }}>
              <h4 style={{ fontSize: "13px", marginBottom: "8px" }}>Ranked Candidate Putaway Bins</h4>
              <table style={{ width: "100%", fontSize: "12px", borderCollapse: "collapse", border: "1px solid #e2e8f0" }}>
                <thead>
                  <tr style={{ background: "#f8fafc", textAlign: "left" }}>
                    <th style={{ padding: "8px" }}>Select</th>
                    <th style={{ padding: "8px" }}>Bin Code</th>
                    <th style={{ padding: "8px" }}>Zone</th>
                    <th style={{ padding: "8px" }}>Score</th>
                    <th style={{ padding: "8px" }}>Occupancy</th>
                    <th style={{ padding: "8px" }}>Features</th>
                    <th style={{ padding: "8px" }}>Evaluation Reasons</th>
                  </tr>
                </thead>
                <tbody>
                  {putawayCandidates.map((c) => (
                    <tr key={c.binId} style={{ borderBottom: "1px solid #f1f5f9" }}>
                      <td style={{ padding: "8px" }}>
                        <input
                          type="radio"
                          name="putawayTarget"
                          checked={selectedPutawayBinId === c.binId}
                          onChange={() => setSelectedPutawayBinId(c.binId)}
                        />
                      </td>
                      <td style={{ padding: "8px", fontWeight: 600 }}>{c.binCode}</td>
                      <td style={{ padding: "8px" }}>{c.zoneCode} ({c.zoneName})</td>
                      <td style={{ padding: "8px", fontWeight: 700, color: "#2563eb" }}>{c.score} pts</td>
                      <td style={{ padding: "8px" }}>
                        {c.currentUnits} / {c.maxUnits ?? "∞"} ({calculateBinUtilization(c.currentUnits, c.maxUnits ?? undefined)}%)
                      </td>
                      <td style={{ padding: "8px" }}>
                        {c.isPickFace && <span style={{ background: "#dbeafe", color: "#1e40af", padding: "2px 6px", borderRadius: "4px", marginRight: "4px" }}>Pick Face</span>}
                        {c.isColdChain && <span style={{ background: "#e0f2fe", color: "#0369a1", padding: "2px 6px", borderRadius: "4px", marginRight: "4px" }}>Cold</span>}
                        {c.isHazardous && <span style={{ background: "#fee2e2", color: "#991b1b", padding: "2px 6px", borderRadius: "4px" }}>Hazard</span>}
                      </td>
                      <td style={{ padding: "8px", fontSize: "11px", color: "#475569" }}>
                        {c.reasons.join("; ")}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <div style={{ marginTop: "16px", display: "flex", gap: "10px", alignItems: "center" }}>
                <label style={{ fontSize: "12px" }}>
                  Source Receiving Dock Bin:
                  <select
                    value={putawaySourceBinId}
                    onChange={(e) => setPutawaySourceBinId(e.target.value)}
                    style={{ padding: "6px", marginLeft: "6px" }}
                  >
                    <option value="">Select Receiving Bin...</option>
                    {bins.map((b) => (
                      <option key={b.id} value={b.id}>
                        {b.binCode} ({b.zoneCode || "No Zone"})
                      </option>
                    ))}
                  </select>
                </label>
                <button
                  type="button"
                  onClick={handleExecutePutaway}
                  disabled={loading || !putawaySourceBinId || !selectedPutawayBinId}
                  style={{
                    padding: "8px 18px",
                    background: "#059669",
                    color: "#fff",
                    border: "none",
                    borderRadius: "5px",
                    cursor: "pointer",
                    fontWeight: 600,
                  }}
                >
                  Confirm & Execute Directed Putaway
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* VIEW 3: FAST BARCODE & GS1 SCANNING */}
      {activeTab === "scanning" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Live Barcode Scanner Wedge</h3>
            <p style={{ fontSize: "12px", color: "#64748b" }}>
              Automatic debounce duplicate prevention (1500ms). Supports GS1-128 composite symbologies and Code 128 prefix notation (BIN:, PROD:, WAVE:, PKG:).
            </p>

            <form onSubmit={handleBarcodeScan} style={{ display: "flex", gap: "8px", marginBottom: "14px" }}>
              <input
                type="text"
                autoFocus
                placeholder="Scan or type barcode (e.g. (01)08901234567890(17)261231(10)LOT100 or BIN:A-01-02)"
                value={barcodeInput}
                onChange={(e) => setBarcodeInput(e.target.value)}
                style={{ flex: 1, padding: "8px", fontSize: "13px" }}
              />
              <button
                type="submit"
                style={{ padding: "8px 16px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "5px", cursor: "pointer", fontWeight: 600 }}
              >
                Scan / Resolve
              </button>
            </form>

            {/* GS1 Breakdown Card */}
            {parsedGs1 && parsedGs1.isValid && (
              <div style={{ background: "#f0fdf4", border: "1px solid #bbf7d0", padding: "12px", borderRadius: "6px", marginBottom: "12px" }}>
                <h4 style={{ margin: "0 0 8px 0", fontSize: "13px", color: "#166534" }}>GS1-128 Application Identifier Breakdown</h4>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "6px", fontSize: "11px" }}>
                  <div><strong>AI (01) GTIN:</strong> {parsedGs1.gtin || "N/A"}</div>
                  <div><strong>AI (10) Batch/Lot:</strong> {parsedGs1.lot || "N/A"}</div>
                  <div><strong>AI (17) Expiry Date:</strong> {parsedGs1.expiryDate || "N/A"}</div>
                  <div><strong>AI (21) Serial:</strong> {parsedGs1.serialNumber || "N/A"}</div>
                  {parsedGs1.netWeightKg !== undefined && <div><strong>AI (310x) Net Wt:</strong> {parsedGs1.netWeightKg} kg</div>}
                  {parsedGs1.quantity !== undefined && <div><strong>AI (37) Count:</strong> {parsedGs1.quantity}</div>}
                </div>
              </div>
            )}

            {prefixedInfo && prefixedInfo.isPrefixed && (
              <div style={{ background: "#eff6ff", border: "1px solid #bfdbfe", padding: "10px", borderRadius: "6px", marginBottom: "12px", fontSize: "12px" }}>
                <strong>Prefixed Code:</strong> Type = <code>{prefixedInfo.type.toUpperCase()}</code>, Code = <code>{prefixedInfo.code}</code>
              </div>
            )}

            <h4 style={{ fontSize: "13px", marginBottom: "8px" }}>Recent Scan Audit Trail</h4>
            <div style={{ maxHeight: "250px", overflowY: "auto", border: "1px solid #e2e8f0", borderRadius: "6px" }}>
              <table style={{ width: "100%", fontSize: "11px", borderCollapse: "collapse" }}>
                <thead>
                  <tr style={{ background: "#f8fafc", textAlign: "left" }}>
                    <th style={{ padding: "6px" }}>Time</th>
                    <th style={{ padding: "6px" }}>Barcode</th>
                    <th style={{ padding: "6px" }}>Entity</th>
                    <th style={{ padding: "6px" }}>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {recentScans.map((s) => (
                    <tr key={s.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                      <td style={{ padding: "6px" }}>{new Date(s.scannedAt).toLocaleTimeString()}</td>
                      <td style={{ padding: "6px", fontFamily: "monospace" }}>{s.scannedBarcode}</td>
                      <td style={{ padding: "6px" }}>{s.resolvedEntityType || "unresolved"}</td>
                      <td style={{ padding: "6px" }}>
                        <span style={{
                          padding: "2px 6px",
                          borderRadius: "4px",
                          background: s.scanStatus === "success" ? "#dcfce7" : "#fee2e2",
                          color: s.scanStatus === "success" ? "#15803d" : "#991b1b",
                          fontWeight: 600,
                        }}>
                          {s.scanStatus}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {/* Barcode Definition Registration */}
          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Register Barcode Definition</h3>
            <form onSubmit={handleRegisterBarcode} style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
              <label style={{ fontSize: "12px" }}>
                Barcode Value:
                <input
                  type="text"
                  required
                  placeholder="e.g. 08901234567890 or PROD:ITEM-001"
                  value={regBarcodeValue}
                  onChange={(e) => setRegBarcodeValue(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                />
              </label>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <label style={{ fontSize: "12px" }}>
                  Entity Type:
                  <select
                    value={regEntityType}
                    onChange={(e) => setRegEntityType(e.target.value)}
                    style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                  >
                    <option value="product">Product Variant</option>
                    <option value="bin">Storage Bin</option>
                    <option value="wave">Picking Wave</option>
                    <option value="package">Package / Carton</option>
                  </select>
                </label>
                <label style={{ fontSize: "12px" }}>
                  Entity ID (UUID):
                  <input
                    type="text"
                    required
                    placeholder="Target entity UUID"
                    value={regEntityId}
                    onChange={(e) => setRegEntityId(e.target.value)}
                    style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                  />
                </label>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <label style={{ fontSize: "12px" }}>
                  GTIN (optional):
                  <input
                    type="text"
                    placeholder="14-digit GTIN"
                    value={regGtin}
                    onChange={(e) => setRegGtin(e.target.value)}
                    style={{ width: "100%", padding: "6px" }}
                  />
                </label>
                <label style={{ fontSize: "12px" }}>
                  Lot / Batch:
                  <input
                    type="text"
                    placeholder="Lot number"
                    value={regLot}
                    onChange={(e) => setRegLot(e.target.value)}
                    style={{ width: "100%", padding: "6px" }}
                  />
                </label>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <label style={{ fontSize: "12px" }}>
                  Expiry Date:
                  <input
                    type="date"
                    value={regExpiry}
                    onChange={(e) => setRegExpiry(e.target.value)}
                    style={{ width: "100%", padding: "6px" }}
                  />
                </label>
                <label style={{ fontSize: "12px" }}>
                  Serial Number:
                  <input
                    type="text"
                    placeholder="Serial number"
                    value={regSerial}
                    onChange={(e) => setRegSerial(e.target.value)}
                    style={{ width: "100%", padding: "6px" }}
                  />
                </label>
              </div>

              <button
                type="submit"
                disabled={loading}
                style={{ padding: "8px", background: "#059669", color: "#fff", border: "none", borderRadius: "5px", cursor: "pointer", fontWeight: 600 }}
              >
                Register Barcode
              </button>
            </form>
          </div>
        </div>
      )}

      {/* VIEW 4: PICKING WAVES & SERPENTINE PATH */}
      {activeTab === "waves" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 2fr", gap: "16px" }}>
          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Create Picking Wave</h3>
            <form onSubmit={handleCreateWave} style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
              <label style={{ fontSize: "12px" }}>
                Wave Number:
                <input
                  type="text"
                  required
                  value={waveNumber}
                  onChange={(e) => setWaveNumber(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                />
              </label>
              <label style={{ fontSize: "12px" }}>
                Wave Type:
                <select
                  value={waveType}
                  onChange={(e) => setWaveType(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                >
                  <option value="batch">Batch Picking</option>
                  <option value="zone">Zone Picking</option>
                  <option value="cluster">Cluster Picking</option>
                  <option value="single_order">Single Order</option>
                </select>
              </label>
              <button
                type="submit"
                disabled={loading}
                style={{ padding: "8px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "5px", cursor: "pointer", fontWeight: 600 }}
              >
                Create Wave
              </button>
            </form>

            <h4 style={{ marginTop: "20px", marginBottom: "8px", fontSize: "13px" }}>Active Waves ({waves.length})</h4>
            <div style={{ maxHeight: "250px", overflowY: "auto", border: "1px solid #e2e8f0", borderRadius: "6px" }}>
              {waves.map((w) => (
                <div
                  key={w.id}
                  onClick={() => setSelectedWaveId(w.id)}
                  style={{
                    padding: "8px 10px",
                    borderBottom: "1px solid #f1f5f9",
                    cursor: "pointer",
                    background: selectedWaveId === w.id ? "#eff6ff" : "#ffffff",
                    borderLeft: selectedWaveId === w.id ? "3px solid #2563eb" : "none",
                  }}
                >
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <strong style={{ fontSize: "12px" }}>{w.waveNumber}</strong>
                    <span style={{ fontSize: "10px", padding: "2px 6px", borderRadius: "4px", background: "#e2e8f0" }}>{w.status}</span>
                  </div>
                  <div style={{ fontSize: "11px", color: "#64748b", marginTop: "2px" }}>
                    Type: {w.waveType} · Lines: {w.pickedLines} / {w.totalLines}
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>
              Serpentine Pick Path Checklist {selectedWave ? `(${selectedWave.waveNumber})` : ""}
            </h3>
            <p style={{ fontSize: "12px", color: "#64748b" }}>
              Optimal S-curve warehouse routing: alternating traversal directions across aisles minimizes picker travel distance.
            </p>

            {selectedWave && (
              <>
                <form onSubmit={handleAddWaveLine} style={{ display: "grid", gridTemplateColumns: "repeat(5, 1fr) auto", gap: "8px", marginBottom: "16px", alignItems: "end" }}>
                  <label style={{ fontSize: "11px" }}>
                    Variant ID:
                    <input
                      type="text"
                      required
                      placeholder="Variant UUID"
                      value={waveLineVariantId}
                      onChange={(e) => setWaveLineVariantId(e.target.value)}
                      style={{ width: "100%", padding: "4px" }}
                    />
                  </label>
                  <label style={{ fontSize: "11px" }}>
                    Product Name:
                    <input
                      type="text"
                      placeholder="e.g. Widget"
                      value={waveLineProductName}
                      onChange={(e) => setWaveLineProductName(e.target.value)}
                      style={{ width: "100%", padding: "4px" }}
                    />
                  </label>
                  <label style={{ fontSize: "11px" }}>
                    SKU:
                    <input
                      type="text"
                      placeholder="e.g. WDG-01"
                      value={waveLineSku}
                      onChange={(e) => setWaveLineSku(e.target.value)}
                      style={{ width: "100%", padding: "4px" }}
                    />
                  </label>
                  <label style={{ fontSize: "11px" }}>
                    Source Bin:
                    <select
                      value={waveLineBinId}
                      onChange={(e) => setWaveLineBinId(e.target.value)}
                      style={{ width: "100%", padding: "4px" }}
                    >
                      <option value="">Select Bin...</option>
                      {bins.map((b) => (
                        <option key={b.id} value={b.id}>{b.binCode} ({b.zoneCode})</option>
                      ))}
                    </select>
                  </label>
                  <label style={{ fontSize: "11px" }}>
                    Qty:
                    <input
                      type="number"
                      value={waveLineQty}
                      onChange={(e) => setWaveLineQty(e.target.value)}
                      style={{ width: "100%", padding: "4px" }}
                    />
                  </label>
                  <button
                    type="submit"
                    disabled={loading}
                    style={{ padding: "6px 12px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "4px", cursor: "pointer", height: "28px" }}
                  >
                    + Line
                  </button>
                </form>

                <div style={{ maxHeight: "300px", overflowY: "auto", border: "1px solid #e2e8f0", borderRadius: "6px", marginBottom: "16px" }}>
                  <table style={{ width: "100%", fontSize: "11px", borderCollapse: "collapse" }}>
                    <thead>
                      <tr style={{ background: "#f8fafc", textAlign: "left" }}>
                        <th style={{ padding: "6px" }}>Seq</th>
                        <th style={{ padding: "6px" }}>Bin Code</th>
                        <th style={{ padding: "6px" }}>Zone / Aisle / Rack</th>
                        <th style={{ padding: "6px" }}>Item</th>
                        <th style={{ padding: "6px" }}>Required</th>
                        <th style={{ padding: "6px" }}>Picked</th>
                        <th style={{ padding: "6px" }}>Status</th>
                        <th style={{ padding: "6px" }}>Action</th>
                      </tr>
                    </thead>
                    <tbody>
                      {activeWaveSequencedLines.map((line) => (
                        <tr key={line.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                          <td style={{ padding: "6px", fontWeight: 700, color: "#2563eb" }}>#{line.pickSequence}</td>
                          <td style={{ padding: "6px", fontWeight: 600 }}>{line.binCode}</td>
                          <td style={{ padding: "6px" }}>{line.zoneCode} / A{line.aisle} / R{line.rack}</td>
                          <td style={{ padding: "6px" }}>{line.productName} ({line.sku})</td>
                          <td style={{ padding: "6px" }}>{line.quantityRequired}</td>
                          <td style={{ padding: "6px" }}>{line.quantityPicked ?? 0}</td>
                          <td style={{ padding: "6px" }}>{line.status}</td>
                          <td style={{ padding: "6px" }}>
                            <button
                              type="button"
                              onClick={() => setPickConfirmLineId(line.id)}
                              style={{ padding: "2px 6px", fontSize: "10px", background: "#f1f5f9", border: "1px solid #cbd5e1", borderRadius: "3px", cursor: "pointer" }}
                            >
                              Confirm
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                {/* Pick Confirmation Barcode Verification */}
                <form onSubmit={handleConfirmPick} style={{ display: "flex", gap: "8px", alignItems: "center", background: "#f8fafc", padding: "10px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                  <span style={{ fontSize: "12px", fontWeight: 600 }}>Confirm Pick:</span>
                  <input
                    type="text"
                    required
                    placeholder="Scan product/bin barcode to verify"
                    value={pickConfirmBarcode}
                    onChange={(e) => setPickConfirmBarcode(e.target.value)}
                    style={{ flex: 1, padding: "6px", fontSize: "12px" }}
                  />
                  <input
                    type="number"
                    value={pickConfirmQty}
                    onChange={(e) => setPickConfirmQty(e.target.value)}
                    style={{ width: "60px", padding: "6px", fontSize: "12px" }}
                  />
                  <button
                    type="submit"
                    disabled={loading || !pickConfirmLineId}
                    style={{ padding: "6px 14px", background: "#059669", color: "#fff", border: "none", borderRadius: "4px", cursor: "pointer", fontWeight: 600 }}
                  >
                    Confirm Scan
                  </button>
                </form>
              </>
            )}
          </div>
        </div>
      )}

      {/* VIEW 5: REPLENISHMENT */}
      {activeTab === "replenishment" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 2fr", gap: "16px" }}>
          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Create Replenishment Rule</h3>
            <form onSubmit={handleCreateReplenRule} style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
              <label style={{ fontSize: "12px" }}>
                Target Pick Face Bin:
                <select
                  value={replenPickBinId}
                  onChange={(e) => setReplenPickBinId(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                >
                  <option value="">Select Pick Bin...</option>
                  {bins.map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.binCode} ({b.isPickFace ? "⭐ Pick Face" : "Standard"})
                    </option>
                  ))}
                </select>
              </label>

              <label style={{ fontSize: "12px" }}>
                Source Reserve Zone (optional):
                <select
                  value={replenReserveZoneId}
                  onChange={(e) => setReplenReserveZoneId(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                >
                  <option value="">Any Reserve Zone</option>
                  {zones.map((z) => (
                    <option key={z.id} value={z.id}>{z.zoneCode} ({z.name})</option>
                  ))}
                </select>
              </label>

              <label style={{ fontSize: "12px" }}>
                Product Variant ID:
                <input
                  type="text"
                  required
                  placeholder="Variant UUID"
                  value={replenVariantId}
                  onChange={(e) => setReplenVariantId(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                />
              </label>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <label style={{ fontSize: "11px" }}>
                  Min Quantity:
                  <input
                    type="number"
                    value={replenMin}
                    onChange={(e) => setReplenMin(e.target.value)}
                    style={{ width: "100%", padding: "4px" }}
                  />
                </label>
                <label style={{ fontSize: "11px" }}>
                  Max Capacity:
                  <input
                    type="number"
                    value={replenMax}
                    onChange={(e) => setReplenMax(e.target.value)}
                    style={{ width: "100%", padding: "4px" }}
                  />
                </label>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <label style={{ fontSize: "11px" }}>
                  Reorder Point:
                  <input
                    type="number"
                    value={replenReorder}
                    onChange={(e) => setReplenReorder(e.target.value)}
                    style={{ width: "100%", padding: "4px" }}
                  />
                </label>
                <label style={{ fontSize: "11px" }}>
                  Increment:
                  <input
                    type="number"
                    value={replenIncrement}
                    onChange={(e) => setReplenIncrement(e.target.value)}
                    style={{ width: "100%", padding: "4px" }}
                  />
                </label>
              </div>

              <button
                type="submit"
                disabled={loading}
                style={{ padding: "8px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "5px", cursor: "pointer", fontWeight: 600 }}
              >
                Save Rule
              </button>
            </form>
          </div>

          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
              <h3 style={{ margin: 0, fontSize: "15px" }}>Replenishment Recommendations</h3>
              <button
                type="button"
                onClick={handleGenerateReplenishment}
                disabled={loading}
                style={{ padding: "6px 14px", background: "#059669", color: "#fff", border: "none", borderRadius: "4px", cursor: "pointer", fontWeight: 600, fontSize: "12px" }}
              >
                Evaluate Reorder Triggers
              </button>
            </div>

            <p style={{ fontSize: "12px", color: "#64748b" }}>
              Evaluates pick face on-hand stock against reorder thresholds. Rounds replenishment deficits to standard pack increments and checks reserve bin availability.
            </p>

            <div style={{ maxHeight: "350px", overflowY: "auto", border: "1px solid #e2e8f0", borderRadius: "6px" }}>
              <table style={{ width: "100%", fontSize: "11px", borderCollapse: "collapse" }}>
                <thead>
                  <tr style={{ background: "#f8fafc", textAlign: "left" }}>
                    <th style={{ padding: "6px" }}>Pick Bin</th>
                    <th style={{ padding: "6px" }}>Current</th>
                    <th style={{ padding: "6px" }}>Reorder Point</th>
                    <th style={{ padding: "6px" }}>Max</th>
                    <th style={{ padding: "6px" }}>Recommended</th>
                    <th style={{ padding: "6px" }}>Reserve Bin</th>
                    <th style={{ padding: "6px" }}>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {replenRecommendations.map((r) => (
                    <tr key={r.ruleId} style={{ borderBottom: "1px solid #f1f5f9" }}>
                      <td style={{ padding: "6px", fontWeight: 600 }}>{r.pickBinCode}</td>
                      <td style={{ padding: "6px" }}>{r.currentPickStock}</td>
                      <td style={{ padding: "6px" }}>{r.reorderPoint}</td>
                      <td style={{ padding: "6px" }}>{r.maxQuantity}</td>
                      <td style={{ padding: "6px", fontWeight: 700, color: "#2563eb" }}>{r.recommendedQuantity}</td>
                      <td style={{ padding: "6px" }}>{r.reserveBinCode || "None Available"}</td>
                      <td style={{ padding: "6px" }}>
                        <button
                          type="button"
                          onClick={() => handleExecuteReplenishment(r)}
                          disabled={loading || !r.reserveBinId}
                          style={{
                            padding: "4px 8px",
                            fontSize: "10px",
                            background: r.reserveBinId ? "#2563eb" : "#cbd5e1",
                            color: "#fff",
                            border: "none",
                            borderRadius: "3px",
                            cursor: r.reserveBinId ? "pointer" : "not-allowed",
                          }}
                        >
                          Execute Transfer
                        </button>
                      </td>
                    </tr>
                  ))}
                  {replenRecommendations.length === 0 && (
                    <tr>
                      <td colSpan={7} style={{ padding: "12px", textAlign: "center", color: "#64748b" }}>
                        All pick face bins are above reorder thresholds or no evaluation has been run yet.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}

      {/* VIEW 6: BIN-TO-BIN TRANSFERS */}
      {activeTab === "transfers" && (
        <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0", maxWidth: "600px" }}>
          <h3 style={{ marginTop: 0, fontSize: "15px" }}>Model A FIFO Bin-to-Bin Transfer</h3>
          <p style={{ fontSize: "12px", color: "#64748b" }}>
            Transfers physical inventory between warehouse locations while strictly preserving original cost layer IDs, receipt timestamps, and unit costs.
          </p>

          <form onSubmit={handleBinTransfer} style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
              <label style={{ fontSize: "12px" }}>
                Source Bin:
                <select
                  required
                  value={transferSourceBinId}
                  onChange={(e) => setTransferSourceBinId(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                >
                  <option value="">Select Source Bin...</option>
                  {bins.map((b) => (
                    <option key={b.id} value={b.id}>{b.binCode} ({b.zoneCode})</option>
                  ))}
                </select>
              </label>

              <label style={{ fontSize: "12px" }}>
                Target Destination Bin:
                <select
                  required
                  value={transferTargetBinId}
                  onChange={(e) => setTransferTargetBinId(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                >
                  <option value="">Select Destination Bin...</option>
                  {bins.map((b) => (
                    <option key={b.id} value={b.id}>{b.binCode} ({b.zoneCode})</option>
                  ))}
                </select>
              </label>
            </div>

            <label style={{ fontSize: "12px" }}>
              Product Variant ID:
              <input
                type="text"
                required
                placeholder="Product variant UUID"
                value={transferVariantId}
                onChange={(e) => setTransferVariantId(e.target.value)}
                style={{ width: "100%", padding: "6px", marginTop: "4px" }}
              />
            </label>

            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
              <label style={{ fontSize: "12px" }}>
                Transfer Quantity:
                <input
                  type="number"
                  min="0.0001"
                  step="any"
                  required
                  value={transferQty}
                  onChange={(e) => setTransferQty(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                />
              </label>

              <label style={{ fontSize: "12px" }}>
                Reason Code:
                <select
                  value={transferReason}
                  onChange={(e) => setTransferReason(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                >
                  <option value="reorganization">Reorganization / Consolidation</option>
                  <option value="pick_face_replenishment">Pick Face Replenishment</option>
                  <option value="staging">Staging for Dispatch</option>
                  <option value="cold_chain_relocation">Cold Chain Relocation</option>
                  <option value="damaged">Damaged Stock Transfer</option>
                </select>
              </label>
            </div>

            <button
              type="submit"
              disabled={loading || transferSourceBinId === transferTargetBinId}
              style={{
                padding: "10px",
                background: transferSourceBinId === transferTargetBinId ? "#cbd5e1" : "#2563eb",
                color: "#fff",
                border: "none",
                borderRadius: "5px",
                cursor: transferSourceBinId === transferTargetBinId ? "not-allowed" : "pointer",
                fontWeight: 600,
                marginTop: "6px",
              }}
            >
              Execute Atomic Bin Transfer
            </button>
          </form>
        </div>
      )}

      {/* VIEW 7: PACKING & DISPATCH */}
      {activeTab === "dispatch" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Create Carton / Package</h3>
            <form onSubmit={handleCreatePackage} style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
              <label style={{ fontSize: "12px" }}>
                Package Number:
                <input
                  type="text"
                  required
                  value={pkgNumber}
                  onChange={(e) => setPkgNumber(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                />
              </label>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <label style={{ fontSize: "12px" }}>
                  Package Type:
                  <select
                    value={pkgType}
                    onChange={(e) => setPkgType(e.target.value)}
                    style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                  >
                    <option value="carton">Standard Carton</option>
                    <option value="box">Small Box</option>
                    <option value="pallet">Wooden Pallet</option>
                    <option value="crate">Insulated Crate</option>
                  </select>
                </label>
                <label style={{ fontSize: "12px" }}>
                  Tare Weight (kg):
                  <input
                    type="number"
                    value={pkgTareWeight}
                    onChange={(e) => setPkgTareWeight(e.target.value)}
                    style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                  />
                </label>
              </div>
              <button
                type="submit"
                disabled={loading}
                style={{ padding: "8px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "5px", cursor: "pointer", fontWeight: 600 }}
              >
                Create Package
              </button>
            </form>

            <h4 style={{ marginTop: "20px", marginBottom: "8px", fontSize: "13px" }}>Packages ({packages.length})</h4>
            <div style={{ maxHeight: "250px", overflowY: "auto", border: "1px solid #e2e8f0", borderRadius: "6px" }}>
              {packages.map((p) => (
                <div
                  key={p.id}
                  onClick={() => setSelectedPkgId(p.id)}
                  style={{
                    padding: "8px 10px",
                    borderBottom: "1px solid #f1f5f9",
                    cursor: "pointer",
                    background: selectedPkgId === p.id ? "#eff6ff" : "#ffffff",
                    borderLeft: selectedPkgId === p.id ? "3px solid #2563eb" : "none",
                  }}
                >
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <strong style={{ fontSize: "12px" }}>{p.packageNumber}</strong>
                    <span style={{ fontSize: "10px", padding: "2px 6px", borderRadius: "4px", background: "#e2e8f0" }}>{p.status}</span>
                  </div>
                  <div style={{ fontSize: "11px", color: "#64748b", marginTop: "2px" }}>
                    Type: {p.packageType} · Tare: {p.tareWeightKg} kg {p.grossWeightKg ? `· Gross: ${p.grossWeightKg} kg` : ""}
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Seal & Dispatch Operations</h3>
            {selectedPkgId ? (
              <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                  <h4 style={{ margin: "0 0 10px 0", fontSize: "13px" }}>Step 1: Scale Weight & Seal</h4>
                  <label style={{ fontSize: "12px", display: "block", marginBottom: "8px" }}>
                    Actual Scale Gross Weight (kg):
                    <input
                      type="number"
                      step="0.01"
                      value={sealGrossWeight}
                      onChange={(e) => setSealGrossWeight(e.target.value)}
                      style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                    />
                  </label>
                  <button
                    type="button"
                    onClick={handleSealPackage}
                    disabled={loading}
                    style={{ padding: "6px 14px", background: "#059669", color: "#fff", border: "none", borderRadius: "4px", cursor: "pointer", fontWeight: 600 }}
                  >
                    Seal Carton
                  </button>
                </div>

                <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                  <h4 style={{ margin: "0 0 10px 0", fontSize: "13px" }}>Step 2: Assign Carrier & Dispatch</h4>
                  <label style={{ fontSize: "12px", display: "block", marginBottom: "8px" }}>
                    Logistics Carrier:
                    <select
                      value={dispatchCarrier}
                      onChange={(e) => setDispatchCarrier(e.target.value)}
                      style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                    >
                      <option value="TCS Express">TCS Express</option>
                      <option value="Leopard Courier">Leopard Courier</option>
                      <option value="DHL Express">DHL Express</option>
                      <option value="FedEx">FedEx</option>
                      <option value="In-House Fleet">In-House Fleet</option>
                    </select>
                  </label>
                  <label style={{ fontSize: "12px", display: "block", marginBottom: "8px" }}>
                    Carrier Tracking Number:
                    <input
                      type="text"
                      placeholder="e.g. TCS-99882211"
                      value={dispatchTracking}
                      onChange={(e) => setDispatchTracking(e.target.value)}
                      style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                    />
                  </label>
                  <button
                    type="button"
                    onClick={handleDispatchPackage}
                    disabled={loading}
                    style={{ padding: "6px 14px", background: "#2563eb", color: "#fff", border: "none", borderRadius: "4px", cursor: "pointer", fontWeight: 600 }}
                  >
                    Mark Dispatched
                  </button>
                </div>
              </div>
            ) : (
              <p style={{ color: "#64748b", fontSize: "12px" }}>Select a package on the left to verify weight or dispatch.</p>
            )}
          </div>
        </div>
      )}

      {/* VIEW 8: QUARANTINE & RETURNS */}
      {activeTab === "quarantine" && (
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "16px" }}>
          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Create Quarantine Hold</h3>
            <form onSubmit={handleCreateQuarantine} style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
              <label style={{ fontSize: "12px" }}>
                Source Bin:
                <select
                  value={qBinId}
                  onChange={(e) => setQBinId(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                >
                  <option value="">Select Bin...</option>
                  {bins.map((b) => (
                    <option key={b.id} value={b.id}>{b.binCode} ({b.zoneCode})</option>
                  ))}
                </select>
              </label>

              <label style={{ fontSize: "12px" }}>
                Product Variant ID:
                <input
                  type="text"
                  required
                  placeholder="Variant UUID"
                  value={qVariantId}
                  onChange={(e) => setQVariantId(e.target.value)}
                  style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                />
              </label>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <label style={{ fontSize: "12px" }}>
                  Quantity:
                  <input
                    type="number"
                    value={qQty}
                    onChange={(e) => setQQty(e.target.value)}
                    style={{ width: "100%", padding: "6px" }}
                  />
                </label>
                <label style={{ fontSize: "12px" }}>
                  Hold Reason:
                  <select
                    value={qReason}
                    onChange={(e) => setQReason(e.target.value)}
                    style={{ width: "100%", padding: "6px" }}
                  >
                    <option value="damaged">Physical Damage</option>
                    <option value="expired">Expired Date</option>
                    <option value="inspection_failed">Quality Inspection Failed</option>
                    <option value="suspected_contamination">Suspected Contamination</option>
                  </select>
                </label>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                <label style={{ fontSize: "12px" }}>
                  Lot / Batch:
                  <input
                    type="text"
                    placeholder="Lot number"
                    value={qLotNumber}
                    onChange={(e) => setQLotNumber(e.target.value)}
                    style={{ width: "100%", padding: "6px" }}
                  />
                </label>
                <label style={{ fontSize: "12px" }}>
                  Quarantine Bin:
                  <select
                    value={qQuarantineBinId}
                    onChange={(e) => setQQuarantineBinId(e.target.value)}
                    style={{ width: "100%", padding: "6px" }}
                  >
                    <option value="">Dedicated Quarantine Bin...</option>
                    {bins.map((b) => (
                      <option key={b.id} value={b.id}>{b.binCode}</option>
                    ))}
                  </select>
                </label>
              </div>

              <button
                type="submit"
                disabled={loading}
                style={{ padding: "8px", background: "#dc2626", color: "#fff", border: "none", borderRadius: "5px", cursor: "pointer", fontWeight: 600 }}
              >
                Place in Quarantine Hold
              </button>
            </form>
          </div>

          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Quarantine Disposition & Release</h3>
            <div style={{ maxHeight: "200px", overflowY: "auto", border: "1px solid #e2e8f0", borderRadius: "6px", marginBottom: "16px" }}>
              {quarantineRecords.map((q) => (
                <div
                  key={q.id}
                  onClick={() => setSelectedQId(q.id)}
                  style={{
                    padding: "8px 10px",
                    borderBottom: "1px solid #f1f5f9",
                    cursor: "pointer",
                    background: selectedQId === q.id ? "#fef2f2" : "#ffffff",
                    borderLeft: selectedQId === q.id ? "3px solid #dc2626" : "none",
                  }}
                >
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    <strong style={{ fontSize: "12px" }}>Hold #{q.id.slice(0, 8)}</strong>
                    <span style={{ fontSize: "10px", padding: "2px 6px", borderRadius: "4px", background: "#fee2e2", color: "#991b1b" }}>{q.status}</span>
                  </div>
                  <div style={{ fontSize: "11px", color: "#64748b", marginTop: "2px" }}>
                    Qty: {q.quantity} · Reason: {q.quarantineReason}
                  </div>
                </div>
              ))}
            </div>

            {selectedQId && (
              <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                <h4 style={{ margin: "0 0 10px 0", fontSize: "13px" }}>Disposition Action</h4>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px", marginBottom: "8px" }}>
                  <label style={{ fontSize: "12px" }}>
                    Decision:
                    <select
                      value={qDisposition}
                      onChange={(e) => setQDisposition(e.target.value)}
                      style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                    >
                      <option value="release_to_stock">Release to Available Stock</option>
                      <option value="scrap">Scrap & Discard (Record Expense)</option>
                      <option value="return_to_vendor">Return to Supplier</option>
                    </select>
                  </label>
                  <label style={{ fontSize: "12px" }}>
                    Target Bin (if release):
                    <select
                      value={qTargetBinId}
                      onChange={(e) => setQTargetBinId(e.target.value)}
                      style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                    >
                      <option value="">Select Target Bin...</option>
                      {bins.map((b) => (
                        <option key={b.id} value={b.id}>{b.binCode}</option>
                      ))}
                    </select>
                  </label>
                </div>
                <label style={{ fontSize: "12px", display: "block", marginBottom: "8px" }}>
                  Notes / QC Sign-off:
                  <input
                    type="text"
                    placeholder="Inspection findings & approval justification"
                    value={qDispNotes}
                    onChange={(e) => setQDispNotes(e.target.value)}
                    style={{ width: "100%", padding: "6px", marginTop: "4px" }}
                  />
                </label>
                <button
                  type="button"
                  onClick={handleDisposeQuarantine}
                  disabled={loading}
                  style={{ padding: "6px 14px", background: "#059669", color: "#fff", border: "none", borderRadius: "4px", cursor: "pointer", fontWeight: 600 }}
                >
                  Finalize Disposition
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      {/* VIEW 9: WMS PERFORMANCE METRICS */}
      {activeTab === "metrics" && (
        <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(5, 1fr)", gap: "12px" }}>
            <div style={{ background: "#ffffff", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <span style={{ fontSize: "11px", color: "#64748b" }}>TOTAL ZONES</span>
              <h3 style={{ margin: "4px 0", fontSize: "20px" }}>{zones.length}</h3>
              <span style={{ fontSize: "11px", color: "#059669" }}>{zones.filter((z) => z.isPickFace).length} Pick Face Zones</span>
            </div>
            <div style={{ background: "#ffffff", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <span style={{ fontSize: "11px", color: "#64748b" }}>TOTAL BINS</span>
              <h3 style={{ margin: "4px 0", fontSize: "20px" }}>{bins.length}</h3>
              <span style={{ fontSize: "11px", color: "#2563eb" }}>{bins.filter((b) => b.isPickFace).length} Active Pick Bins</span>
            </div>
            <div style={{ background: "#ffffff", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <span style={{ fontSize: "11px", color: "#64748b" }}>ACTIVE WAVES</span>
              <h3 style={{ margin: "4px 0", fontSize: "20px" }}>{waves.length}</h3>
              <span style={{ fontSize: "11px", color: "#64748b" }}>Serpentine Routing Enabled</span>
            </div>
            <div style={{ background: "#ffffff", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <span style={{ fontSize: "11px", color: "#64748b" }}>REPLENISHMENT RULES</span>
              <h3 style={{ margin: "4px 0", fontSize: "20px" }}>{replenishmentRules.length}</h3>
              <span style={{ fontSize: "11px", color: "#d97706" }}>Min/Max Auto-Monitoring</span>
            </div>
            <div style={{ background: "#ffffff", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
              <span style={{ fontSize: "11px", color: "#64748b" }}>QUARANTINE HOLDS</span>
              <h3 style={{ margin: "4px 0", fontSize: "20px" }}>{quarantineRecords.filter((q) => q.status === "on_hold").length}</h3>
              <span style={{ fontSize: "11px", color: "#dc2626" }}>Pending QC Review</span>
            </div>
          </div>

          <div style={{ background: "#ffffff", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
            <h3 style={{ marginTop: 0, fontSize: "15px" }}>Zone Capacity & Utilization Summary</h3>
            <div style={{ display: "flex", flexDirection: "column", gap: "12px", marginTop: "12px" }}>
              {zones.map((z) => {
                const zoneBins = bins.filter((b) => b.zoneId === z.id);
                const totalCapacity = zoneBins.reduce((acc, b) => acc + (b.maxUnits || 100), 0);
                const occupied = zoneBins.reduce((acc, b) => acc + (b.currentUnits || 0), 0);
                const util = totalCapacity > 0 ? Math.round((occupied / totalCapacity) * 100) : 0;
                return (
                  <div key={z.id}>
                    <div style={{ display: "flex", justifyContent: "space-between", fontSize: "12px", marginBottom: "4px" }}>
                      <span><strong>{z.zoneCode}</strong> — {z.name} ({zoneBins.length} bins)</span>
                      <span>{occupied} / {totalCapacity} units ({util}%)</span>
                    </div>
                    <div style={{ width: "100%", height: "8px", background: "#f1f5f9", borderRadius: "4px", overflow: "hidden" }}>
                      <div
                        style={{
                          width: `${Math.min(100, util)}%`,
                          height: "100%",
                          background: util > 85 ? "#ef4444" : util > 60 ? "#f59e0b" : "#10b981",
                        }}
                      />
                    </div>
                  </div>
                );
              })}
              {zones.length === 0 && (
                <p style={{ color: "#64748b", fontSize: "12px" }}>No zones registered yet.</p>
              )}
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
