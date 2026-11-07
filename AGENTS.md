<!-- CODEGRAPH_START -->
## CodeGraph

When an index is available and useful, consult CodeGraph or Graphify to understand or locate code, trace relationships, assess impact, or explore architecture. Prefer CodeGraph for focused symbol and code questions; use Graphify for broader communities and relationships or when CodeGraph lacks context. Use both when they provide complementary evidence. For simple tasks where a graph query adds no value, use the appropriate direct tool instead.

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.

`SourceMain5.2/` is ignored by Git and has separate local CodeGraph and Graphify indexes. For questions about that reference code, run the graph commands with `SourceMain5.2/` as the working directory; the root indexes do not include it.
<!-- CODEGRAPH_END -->

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- When graphify-out/graph.json exists, use `graphify query "<question>"` for broader community and relationship questions, or when CodeGraph does not provide enough context. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after incremental updates; dirty graph files alone are not a reason to skip graphify. If an index is stale or incomplete, verify findings against source and refresh it when appropriate.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
