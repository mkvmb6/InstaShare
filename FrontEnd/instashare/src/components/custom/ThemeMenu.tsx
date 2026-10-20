import { Monitor, Moon, Sun } from "lucide-react";
import { useTheme } from "next-themes";
import { Button } from "../ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuTrigger } from "../ui/dropdown-menu";

/** Light, dark, or follow the device. The icon reflects what's actually shown, including for "system". */
const ThemeMenu: React.FC = () => {
    const { theme, setTheme, resolvedTheme } = useTheme();
    const Icon = resolvedTheme === 'dark' ? Moon : Sun;
    return (
        <DropdownMenu>
            <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" aria-label="Theme" title="Theme">
                    <Icon />
                </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
                <DropdownMenuRadioGroup value={theme ?? 'system'} onValueChange={setTheme}>
                    <DropdownMenuRadioItem value="light"><Sun /> Light</DropdownMenuRadioItem>
                    <DropdownMenuRadioItem value="dark"><Moon /> Dark</DropdownMenuRadioItem>
                    <DropdownMenuRadioItem value="system"><Monitor /> System</DropdownMenuRadioItem>
                </DropdownMenuRadioGroup>
            </DropdownMenuContent>
        </DropdownMenu>
    );
};

export default ThemeMenu;
