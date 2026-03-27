##############################################################################
# Nikola configuration — uips-decisions
##############################################################################

BLOG_TITLE = "UiPath Studio — Decision Implementations"
BLOG_DESCRIPTION = "Educational reference for implementing business decision logic in UiPath Studio"
BLOG_AUTHOR = "Christian Prior-Mamulyan"
BLOG_EMAIL = ""
SITE_URL = "https://cprima-playground.github.io/uips-decisions/"

OUTPUT_FOLDER = "dist"
THEME = "uips"

# Pure pages site — no blog posts; disable blog index to avoid index.html conflict
POSTS = ()
DISABLED_PLUGINS = [
    "classify_page_index", "classify_sections",
    "classify_archive",
    "classify_authors",
    "classify_categories", "classify_tags",
    "robots",
]
INDEX_PATH = "blog"
PAGES = (
    ("docs/*.md",                   "",                "page.tmpl"),
    ("docs/scenarios/*/*.md",       "scenarios",       "page.tmpl"),
    ("docs/scenarios/*/slides.md",  "scenarios",       "slides.tmpl"),
    ("docs/slides/*.md",            "slides",          "slides.tmpl"),
)

# Compilers
COMPILERS = {
    "markdown": (".md", ".mdown", ".markdown"),
    "rest":     (".rst", ".txt"),
    "html":     (".html", ".htm"),
}

# reveal.js — JS from CDN, plugins/theme local
REVEALJS_SOURCE = "https://cdn.jsdelivr.net/npm/reveal.js@5"
GLOBAL_CONTEXT = {
    "REVEALJS_SOURCE": REVEALJS_SOURCE,
}

# Output
USE_BUNDLES = False
SHOW_SOURCELINK = False
CREATE_FULL_ARCHIVES = False
GENERATE_RSS = False
GENERATE_ATOM = False

# Navigation
NAVIGATION_LINKS = {
    "en": (
        ("/", "Home"),
        ("/scenarios/", "Scenarios"),
        ("/slides/", "Slides"),
    ),
}

DEFAULT_LANG = "en"
TRANSLATIONS = {"en": ""}
