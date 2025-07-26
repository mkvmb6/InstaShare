import { useTheme } from "next-themes";
import { useEffect, useState } from "react";

const TextFilePreview: React.FC<{ url: string }> = ({ url }) => {
    const [content, setContent] = useState<string>("");
    const { theme } = useTheme();
    useEffect(() => {
        fetch(url)
            .then(res => res.text())
            .then(setContent)
            .catch(() => setContent("Failed to load content."));
    }, [url]);
    const isDark = theme === "dark";
    return (
        <pre style={{
            width: "100%",
            height: "70vh",
            background: isDark ? "#222" : "#f5f5f5",
            color: isDark ? "#eee" : "#222",
            padding: "1rem",
            borderRadius: "0.5rem",
            overflow: "auto",
            fontSize: "1rem"
        }}>{content}</pre>
    );
};

export default TextFilePreview;