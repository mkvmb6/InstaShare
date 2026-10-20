import { Link } from "react-router-dom";
import { Fragment } from "react";
import { Breadcrumb, BreadcrumbItem, BreadcrumbLink, BreadcrumbList, BreadcrumbPage, BreadcrumbSeparator } from "../ui/breadcrumb";

/**
 * `segments` are the folder names from the share's root to the current folder; the last one is the current page.
 */
const Breadcrumbs: React.FC<{ segments: string[]; hrefFor: (depth: number) => string }> = ({ segments, hrefFor }) => {
    if (segments.length < 2) return null;
    return (
        <Breadcrumb className="mb-4">
            <BreadcrumbList>
                {segments.map((segment, index) => {
                    const isLast = index === segments.length - 1;
                    return (
                        <Fragment key={index}>
                            {index > 0 && <BreadcrumbSeparator />}
                            <BreadcrumbItem className="max-w-[12rem] sm:max-w-xs">
                                {isLast ? (
                                    <BreadcrumbPage className="truncate">{segment}</BreadcrumbPage>
                                ) : (
                                    <BreadcrumbLink asChild>
                                        <Link to={hrefFor(index)} className="truncate">{segment}</Link>
                                    </BreadcrumbLink>
                                )}
                            </BreadcrumbItem>
                        </Fragment>
                    );
                })}
            </BreadcrumbList>
        </Breadcrumb>
    );
};

export default Breadcrumbs;
