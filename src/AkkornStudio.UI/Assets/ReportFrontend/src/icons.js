import { h } from "vue";
import {
  AlertTriangle,
  ArrowDownWideNarrow,
  ArrowDownAZ,
  ArrowUpDown,
  ArrowUpAZ,
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  CircleDot,
  ClipboardList,
  Clock3,
  Code2,
  Columns3,
  Database,
  Download,
  Expand,
  FileJson2,
  Funnel,
  Group,
  GripVertical,
  Link2,
  ListFilter,
  Moon,
  Pin,
  Plus,
  Rows3,
  RotateCcw,
  Search,
  Save,
  Sigma,
  Sun,
  TextCursorInput,
  Trash2,
  Upload,
  X
} from "lucide-vue-next";

const iconMap = {
  moon: Moon,
  sun: Sun,
  clipboard: ClipboardList,
  code: Code2,
  download: Download,
  json: FileJson2,
  filter: ListFilter,
  sort: ArrowDownWideNarrow,
  sortAsc: ArrowUpAZ,
  sortDesc: ArrowDownAZ,
  sortAppend: ArrowUpDown,
  columns: Columns3,
  rotate: RotateCcw,
  chevronDown: ChevronDown,
  plus: Plus,
  x: X,
  check: Check,
  text: TextCursorInput,
  funnel: Funnel,
  arrowUpDown: ArrowUpDown,
  left: ChevronLeft,
  right: ChevronRight,
  up: ChevronUp,
  expand: Expand,
  rows: Rows3,
  clock: Clock3,
  database: Database,
  alert: AlertTriangle,
  rowSelect: CircleDot,
  search: Search,
  resize: GripVertical,
  pin: Pin,
  save: Save,
  trash: Trash2,
  uploadPreset: Upload,
  downloadPreset: FileJson2,
  group: Group,
  stats: Sigma,
  link: Link2
};

export const IconGlyph = {
  name: "IconGlyph",
  props: {
    name: { type: String, required: true },
    title: { type: String, default: "" },
    size: { type: Number, default: 16 },
    strokeWidth: { type: Number, default: 1.9 }
  },
  render() {
    const IconComponent = iconMap[this.name] ?? CircleDot;
    return h(
      "span",
      {
        class: "icon-glyph",
        title: this.title,
        "aria-hidden": "true"
      },
      [
        h(IconComponent, {
          size: this.size,
          strokeWidth: this.strokeWidth
        })
      ]
    );
  }
};
