export interface ToolMetadata {
    name: string;
    tagline: string;
    description: string;
    githubUrl: string;
    websiteUrl?: string;
    author: string;
    authorGitHub: string;
    tags: string;
    language?: string;
    license?: string;
}

const BASE_URL = "https://github.com/shanselman/TinyToolTown/issues/new";
const TEMPLATE = "submit-tool.yml";

/**
 * Builds a pre-filled GitHub issue URL for the Tiny Tool Town submission template.
 */
export function buildIssueUrl(metadata: ToolMetadata): string {
    const params: string[] = [
        `template=${encodeURIComponent(TEMPLATE)}`,
        `title=${encodeURIComponent(`[Tool] ${metadata.name}`)}`,
        `labels=${encodeURIComponent("new-tool")}`,
        `name=${encodeURIComponent(metadata.name)}`,
        `tagline=${encodeURIComponent(metadata.tagline)}`,
        `description=${encodeURIComponent(metadata.description)}`,
        `github_url=${encodeURIComponent(metadata.githubUrl)}`,
        `author=${encodeURIComponent(metadata.author)}`,
        `author_github=${encodeURIComponent(metadata.authorGitHub)}`,
        `tags=${encodeURIComponent(metadata.tags)}`,
    ];

    if (metadata.websiteUrl) {
        params.push(`website_url=${encodeURIComponent(metadata.websiteUrl)}`);
    }
    if (metadata.language) {
        params.push(`language=${encodeURIComponent(metadata.language)}`);
    }
    if (metadata.license) {
        params.push(`license=${encodeURIComponent(metadata.license)}`);
    }

    return `${BASE_URL}?${params.join("&")}`;
}
