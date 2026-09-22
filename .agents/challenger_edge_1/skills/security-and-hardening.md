# Security and Hardening Skill Notes
Source: C:\Users\nqtha\.gemini\config\skills\security-and-hardening\SKILL.md

Key Principles:
- Treat every external input as hostile.
- Magic bytes validation instead of trusting extension/mimetype.
- Enforce strict size limits at API boundary.
- Prevent Path Traversal (`Path.Combine`, normalize, verify base path).
- Two-phase cleanup to ensure atomic consistency with DB transactions.
