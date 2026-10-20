import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

/** Loaded on demand: the Markdown parser is most of the app's size, and most shares never need it. */
const MarkdownView: React.FC<{ text: string }> = ({ text }) => (
    <article className="prose mx-auto max-w-3xl px-6 py-6 text-sm leading-relaxed sm:text-base">
        {/* Raw HTML in the file is shown as text, never run. */}
        <ReactMarkdown remarkPlugins={[remarkGfm]}>{text}</ReactMarkdown>
    </article>
);

export default MarkdownView;
