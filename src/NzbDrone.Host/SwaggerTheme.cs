namespace NzbDrone.Host;

public static class SwaggerTheme
{
    public const string Css = """
/* Seedarr Dark Theme for Swagger UI */
body {
  background-color: #222018 !important;
  color: #f8f6f0 !important;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif !important;
  margin: 0;
  padding: 0;
}

.swagger-ui {
  color: #f8f6f0 !important;
}

/* Scrollbars */
::-webkit-scrollbar {
  width: 8px;
  height: 8px;
}
::-webkit-scrollbar-track {
  background: #222018;
}
::-webkit-scrollbar-thumb {
  background: #3a352e;
  border-radius: 4px;
}
::-webkit-scrollbar-thumb:hover {
  background: #4a4438;
}

/* Topbar */
.swagger-ui .topbar {
  background-color: #1a1815 !important;
  border-bottom: 1px solid #3a352e !important;
  padding: 10px 0 !important;
}

.swagger-ui .topbar .topbar-wrapper {
  max-width: 1400px !important;
}

.swagger-ui .topbar a {
  color: #c8a84e !important;
  font-weight: 700 !important;
}

.swagger-ui .topbar .download-url-wrapper input[type=text] {
  background-color: #2a2620 !important;
  border: 1px solid #3a352e !important;
  color: #f8f6f0 !important;
  border-radius: 4px !important;
}

.swagger-ui .topbar .download-url-wrapper .download-url-button {
  background-color: #c8a84e !important;
  color: #1a1815 !important;
  border: none !important;
  font-weight: 600 !important;
  border-radius: 4px !important;
}

/* Info Section */
.swagger-ui .info {
  margin: 30px 0 !important;
}

.swagger-ui .info .title {
  color: #c8a84e !important;
  font-size: 2rem !important;
  font-weight: 700 !important;
}

.swagger-ui .info .title small.version-stamp {
  background-color: #c8a84e !important;
  color: #1a1815 !important;
  border-radius: 4px !important;
  padding: 2px 8px !important;
  font-weight: 600 !important;
}

.swagger-ui .info p,
.swagger-ui .info li,
.swagger-ui .info table {
  color: #d4ccbe !important;
}

.swagger-ui .info a {
  color: #c8a84e !important;
}

/* Scheme & Authorization Bar */
.swagger-ui .scheme-container {
  background-color: #2a2620 !important;
  box-shadow: none !important;
  border: 1px solid #3a352e !important;
  border-radius: 8px !important;
  padding: 15px 20px !important;
  margin-bottom: 25px !important;
}

.swagger-ui .schemes-title {
  color: #f8f6f0 !important;
}

.swagger-ui .btn.authorize {
  background-color: transparent !important;
  color: #c8a84e !important;
  border-color: #c8a84e !important;
  border-radius: 6px !important;
  font-weight: 600 !important;
  transition: all 0.2s ease !important;
}

.swagger-ui .btn.authorize:hover {
  background-color: rgba(200, 168, 78, 0.15) !important;
}

.swagger-ui .btn.authorize svg {
  fill: #c8a84e !important;
}

/* Filter & Search */
.swagger-ui .filter .operation-filter-input {
  background-color: #2a2620 !important;
  border: 1px solid #3a352e !important;
  color: #f8f6f0 !important;
  border-radius: 6px !important;
  padding: 8px 12px !important;
}

/* Tag & Section Headers */
.swagger-ui .opblock-tag {
  color: #f8f6f0 !important;
  border-bottom: 1px solid #3a352e !important;
  font-size: 1.25rem !important;
}

.swagger-ui .opblock-tag small {
  color: #9c9484 !important;
}

.swagger-ui .opblock-tag:hover {
  background-color: rgba(200, 168, 78, 0.05) !important;
}

/* Operations / Opblocks */
.swagger-ui .opblock {
  background-color: #2a2620 !important;
  border-radius: 8px !important;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.2) !important;
  margin-bottom: 14px !important;
}

.swagger-ui .opblock .opblock-summary {
  border-bottom: 1px solid transparent !important;
  padding: 8px 14px !important;
}

.swagger-ui .opblock .opblock-summary-path {
  color: #f8f6f0 !important;
  font-weight: 600 !important;
  font-family: monospace !important;
}

.swagger-ui .opblock .opblock-summary-path__deprecated {
  color: #9c9484 !important;
}

.swagger-ui .opblock .opblock-summary-description {
  color: #9c9484 !important;
  font-size: 0.85rem !important;
}

/* HTTP Methods styling */
.swagger-ui .opblock.opblock-get {
  border: 1px solid rgba(74, 144, 226, 0.4) !important;
  background-color: rgba(74, 144, 226, 0.06) !important;
}
.swagger-ui .opblock.opblock-get .opblock-summary-method {
  background-color: #4a90e2 !important;
  border-radius: 4px !important;
  font-weight: 700 !important;
}

.swagger-ui .opblock.opblock-post {
  border: 1px solid rgba(138, 154, 58, 0.4) !important;
  background-color: rgba(138, 154, 58, 0.06) !important;
}
.swagger-ui .opblock.opblock-post .opblock-summary-method {
  background-color: #8a9a3a !important;
  border-radius: 4px !important;
  font-weight: 700 !important;
}

.swagger-ui .opblock.opblock-put {
  border: 1px solid rgba(212, 132, 58, 0.4) !important;
  background-color: rgba(212, 132, 58, 0.06) !important;
}
.swagger-ui .opblock.opblock-put .opblock-summary-method {
  background-color: #d4843a !important;
  border-radius: 4px !important;
  font-weight: 700 !important;
}

.swagger-ui .opblock.opblock-delete {
  border: 1px solid rgba(181, 68, 58, 0.4) !important;
  background-color: rgba(181, 68, 58, 0.06) !important;
}
.swagger-ui .opblock.opblock-delete .opblock-summary-method {
  background-color: #b5443a !important;
  border-radius: 4px !important;
  font-weight: 700 !important;
}

.swagger-ui .opblock.opblock-patch {
  border: 1px solid rgba(200, 168, 78, 0.4) !important;
  background-color: rgba(200, 168, 78, 0.06) !important;
}
.swagger-ui .opblock.opblock-patch .opblock-summary-method {
  background-color: #c8a84e !important;
  color: #1a1815 !important;
  border-radius: 4px !important;
  font-weight: 700 !important;
}

/* Opblock Body & Tables */
.swagger-ui .opblock-body {
  background-color: #222018 !important;
  border-top: 1px solid #3a352e !important;
  padding: 15px !important;
}

.swagger-ui .opblock-description-wrapper p,
.swagger-ui .opblock-external-docs-wrapper p,
.swagger-ui .opblock-title_normal p {
  color: #d4ccbe !important;
}

.swagger-ui table {
  color: #f8f6f0 !important;
}

.swagger-ui table thead tr th {
  color: #9c9484 !important;
  border-bottom: 1px solid #3a352e !important;
}

.swagger-ui table tbody tr td {
  border-bottom: 1px solid #302c24 !important;
  color: #d4ccbe !important;
}

.swagger-ui .parameter__name {
  color: #f8f6f0 !important;
  font-weight: 600 !important;
}

.swagger-ui .parameter__type {
  color: #c8a84e !important;
}

.swagger-ui .parameter__deprecated {
  color: #b5443a !important;
}

.swagger-ui .parameter__in {
  color: #9c9484 !important;
}

/* Form Controls */
.swagger-ui input[type=text],
.swagger-ui input[type=password],
.swagger-ui input[type=search],
.swagger-ui input[type=email],
.swagger-ui select,
.swagger-ui textarea {
  background-color: #2a2620 !important;
  border: 1px solid #3a352e !important;
  color: #f8f6f0 !important;
  border-radius: 4px !important;
  padding: 6px 10px !important;
}

.swagger-ui input:focus,
.swagger-ui select:focus,
.swagger-ui textarea:focus {
  border-color: #c8a84e !important;
  outline: none !important;
}

/* Buttons */
.swagger-ui .btn {
  border-radius: 4px !important;
  font-weight: 600 !important;
}

.swagger-ui .btn.execute {
  background-color: #c8a84e !important;
  color: #1a1815 !important;
  border: none !important;
}

.swagger-ui .btn.execute:hover {
  background-color: #dbbc62 !important;
}

.swagger-ui .btn.btn-clear {
  background-color: #3a352e !important;
  color: #f8f6f0 !important;
  border-color: #4a4438 !important;
}

.swagger-ui .btn.try-out__btn {
  background-color: transparent !important;
  color: #c8a84e !important;
  border-color: #c8a84e !important;
}

.swagger-ui .btn.try-out__btn:hover {
  background-color: rgba(200, 168, 78, 0.15) !important;
}

/* Responses & Code Highlight */
.swagger-ui .responses-inner {
  background-color: transparent !important;
}

.swagger-ui .responses-table {
  background-color: transparent !important;
}

.swagger-ui .response-col_status {
  color: #f8f6f0 !important;
  font-weight: 700 !important;
}

.swagger-ui .response-col_description {
  color: #d4ccbe !important;
}

.swagger-ui .highlight-code,
.swagger-ui .microlight {
  background-color: #1a1815 !important;
  color: #f8f6f0 !important;
  border-radius: 6px !important;
  border: 1px solid #302c24 !important;
  padding: 10px !important;
}

.swagger-ui pre {
  background-color: #1a1815 !important;
  color: #f8f6f0 !important;
}

.swagger-ui code {
  color: #c8a84e !important;
}

/* Models / Schemas Section */
.swagger-ui section.models {
  border: 1px solid #3a352e !important;
  border-radius: 8px !important;
  background-color: #2a2620 !important;
}

.swagger-ui section.models h4 {
  color: #c8a84e !important;
  border-bottom: 1px solid #3a352e !important;
}

.swagger-ui section.models .model-container {
  background-color: #222018 !important;
  border-bottom: 1px solid #302c24 !important;
  margin: 0 !important;
  padding: 10px 15px !important;
}

.swagger-ui .model-box {
  background-color: transparent !important;
}

.swagger-ui .model {
  color: #d4ccbe !important;
}

.swagger-ui .model-title {
  color: #f8f6f0 !important;
  font-weight: 600 !important;
}

.swagger-ui .prop-type {
  color: #c8a84e !important;
}

.swagger-ui .prop-format {
  color: #9c9484 !important;
}

/* Dialogs & Modals (Authorize Dialog) */
.swagger-ui .dialog-ux .backdrop-ux {
  background-color: rgba(0, 0, 0, 0.75) !important;
}

.swagger-ui .dialog-ux .modal-ux {
  background-color: #2a2620 !important;
  border: 1px solid #3a352e !important;
  border-radius: 8px !important;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.5) !important;
}

.swagger-ui .dialog-ux .modal-ux-header {
  border-bottom: 1px solid #3a352e !important;
}

.swagger-ui .dialog-ux .modal-ux-header h3 {
  color: #c8a84e !important;
}

.swagger-ui .dialog-ux .modal-ux-content {
  color: #d4ccbe !important;
}

.swagger-ui .dialog-ux .modal-ux-content h4 {
  color: #f8f6f0 !important;
}

.swagger-ui .auth-container {
  border-bottom: 1px solid #302c24 !important;
  padding-bottom: 15px !important;
}

.swagger-ui .auth-btn-wrapper {
  margin-top: 15px !important;
}
""";
}
