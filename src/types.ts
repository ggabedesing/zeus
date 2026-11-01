export interface Snapshot {
  computer?: string; cpu_name?: string | null; supported?: boolean;
  cpu_pct?: number | null; ram_used_pct?: number | null; ram_total_gb?: number | null;
  ram_available_gb?: number | null; disk_free_gb?: number | null; disk_free_pct?: number | null;
  disk_total_gb?: number | null; ac_connected?: boolean | null;
  temp_bytes?: number; temp_count?: number; temp_partial?: boolean;
  startup?: { name: string; enabled: boolean }[]; read_errors?: string[];
}
export interface Adjustment {
  id: string; action_id: string; title: string; description: string; tradeoff: string;
  category?: string; default?: boolean; reversible?: boolean; bytes?: number;
}
export interface Report {
  id: string; created_at: number; goal: string; status: string; snapshot: Snapshot;
  actions: Adjustment[];
  notices: { id: string; title: string; description: string; uri: string; label: string }[];
}
export interface Run {
  id: string; created_at: number; goal: string; status: string; before: Snapshot;
  after: Snapshot | null; undone: boolean;
  results: { title: string; ok: boolean; message?: string; transaction_id?: string;
    freed_bytes?: number; deleted?: number; skipped?: number; reversible?: boolean }[];
  undo_results?: { ok: boolean; message?: string }[];
}
