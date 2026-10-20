import { ArrowUpDown, Check, LayoutGrid, List, Search } from "lucide-react";
import { Button } from "../ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuTrigger } from "../ui/dropdown-menu";
import { Checkbox } from "../ui/checkbox";

export type SortKey = 'name' | 'size' | 'type';

const SORT_LABELS: Record<SortKey, string> = { name: 'Name', size: 'Size', type: 'Type' };

const Toolbar: React.FC<{
    query: string;
    onQuery: (q: string) => void;
    sort: SortKey;
    onSort: (s: SortKey) => void;
    view: 'list' | 'grid';
    onView: (v: 'list' | 'grid') => void;
    selectable: number;
    selected: number;
    onToggleAll: () => void;
}> = ({ query, onQuery, sort, onSort, view, onView, selectable, selected, onToggleAll }) => (
    <div className="mb-3 flex flex-wrap items-center gap-2">
        {selectable > 0 && (
            <label className="flex h-9 items-center gap-2 rounded-md px-2 text-sm hover:bg-accent/60">
                <Checkbox
                    checked={selected === 0 ? false : selected === selectable ? true : 'indeterminate'}
                    onCheckedChange={onToggleAll}
                    aria-label="Select all files"
                />
                <span className="hidden sm:inline">{selected > 0 ? `${selected} selected` : 'Select all'}</span>
            </label>
        )}

        <div className="relative min-w-40 flex-1">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
            <input
                type="search"
                value={query}
                onChange={(e) => onQuery(e.target.value)}
                placeholder="Search this folder"
                aria-label="Search this folder"
                className="h-9 w-full rounded-md border bg-background pl-8 pr-3 text-sm outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
            />
        </div>

        <DropdownMenu>
            <DropdownMenuTrigger asChild>
                <Button variant="outline" size="sm" className="h-9" aria-label={`Sort by ${SORT_LABELS[sort]}`}>
                    <ArrowUpDown /> <span className="hidden sm:inline">{SORT_LABELS[sort]}</span>
                </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
                <DropdownMenuRadioGroup value={sort} onValueChange={(v) => onSort(v as SortKey)}>
                    {(Object.keys(SORT_LABELS) as SortKey[]).map((key) => (
                        <DropdownMenuRadioItem key={key} value={key}>{SORT_LABELS[key]}</DropdownMenuRadioItem>
                    ))}
                </DropdownMenuRadioGroup>
            </DropdownMenuContent>
        </DropdownMenu>

        <div className="flex rounded-md border" role="radiogroup" aria-label="View">
            {(['list', 'grid'] as const).map((v) => {
                const Icon = v === 'list' ? List : LayoutGrid;
                return (
                    <Button key={v} variant="ghost" size="icon" role="radio" aria-checked={view === v}
                            aria-label={v === 'list' ? 'List view' : 'Grid view'} title={v === 'list' ? 'List' : 'Grid'}
                            onClick={() => onView(v)}
                            className={view === v ? "bg-accent text-accent-foreground" : "text-muted-foreground"}>
                        <Icon />
                    </Button>
                );
            })}
        </div>
        <span className="sr-only" aria-live="polite">{selected > 0 ? `${selected} selected` : ''}</span>
        <Check className="hidden" aria-hidden />
    </div>
);

export default Toolbar;
