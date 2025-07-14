import { Link } from "react-router-dom";
import { Breadcrumb, BreadcrumbItem, BreadcrumbLink, BreadcrumbSeparator } from "../ui/breadcrumb";
import { Fragment } from "react/jsx-runtime";

const Breadcrumbs: React.FC<{ folderId: string; currentPath: string }> = ({ folderId, currentPath }) => {
    const segments = currentPath.split('/').filter(Boolean);
    return (
        <Breadcrumb className="mb-4 flex flex-wrap items-center gap-1">
            <BreadcrumbItem>
                <BreadcrumbLink asChild>
                    <Link to={`/view/${folderId}`}>{folderId}</Link>
                </BreadcrumbLink>
            </BreadcrumbItem>
            {segments.map((segment, index) => {
                const path = segments.slice(0, index + 1).join('/');
                return (
                    <Fragment key={index}>
                        <BreadcrumbSeparator className="flex" />
                        <BreadcrumbItem>
                            <BreadcrumbLink asChild>
                                <Link to={`/view/${folderId}/${encodeURIComponent(path)}`}>{segment}</Link>
                            </BreadcrumbLink>
                        </BreadcrumbItem>
                    </Fragment>
                );
            })}
        </Breadcrumb>
    );
};

export default Breadcrumbs;