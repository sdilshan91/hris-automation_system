---
id: TC-CHR-192
user_story: US-CHR-008
module: Core HR
priority: critical
type: functional
status: draft
created: 2026-06-12
---

# TC-CHR-192: Upload valid 5 MB PDF -- stored at tenant/employee-prefixed path with metadata row and appears in document list (happy path)

## 1. Test Objective
Verify that an HR Officer can upload a valid 5 MB PDF document on an employee's Documents tab: the file is stored in tenant-isolated object storage at the path `{tenantId}/core-hr/{employeeId}/{yyyy}/{mm}/{filename}`, a metadata record is created in the `employee_documents` table with the correct `tenant_id`, and the document appears in the employee's categorized document list with all expected fields. This validates AC-1 and AC-2 end-to-end.

## 2. Related Requirements
- User Story: US-CHR-008
- Acceptance Criteria: AC-1, AC-2
- Functional Requirements: FR-1, FR-2, FR-3, FR-9
- Non-Functional Requirements: NFR-2
- Business Rules: BR-1

## 3. Preconditions
- Tenant "acme" exists with status `active` and subdomain `acme.yourhrm.com`.
- An HR Officer user is authenticated in the "acme" tenant context.
- Employee "Jane Doe" (employee_id = `emp-001-uuid`) exists in tenant "acme".
- Object storage (MinIO / Azure Blob / S3) is configured and accessible.
- ClamAV or equivalent virus scanner is running.
- No document named "employment-contract.pdf" exists for this employee.

## 4. Test Data
| Field | Value | Notes |
|-------|-------|-------|
| Subdomain | acme.yourhrm.com | Active tenant |
| User Role | HR Officer | Can upload documents (BR-1) |
| Employee | Jane Doe (emp-001-uuid) | Existing employee |
| File | employment-contract.pdf | 5 MB, valid PDF |
| Category | Contract | From dropdown: Contract, ID, Certificate, Other |
| Description | Signed employment contract 2026 | Optional text |
| Expiry Date | 2027-06-12 | Optional, 1 year from now |

## 5. Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | Navigate to `https://acme.yourhrm.com/employees/emp-001-uuid` and click the "Documents" tab. | The Documents section loads showing a categorized document list (possibly empty) and an "Upload Document" button. |
| 2 | Click "Upload Document". | A form/modal appears with: file drop zone (drag-and-drop or file picker), Category dropdown (Contract, ID, Certificate, Other), Description text field, and Expiry Date date picker. |
| 3 | Drag and drop "employment-contract.pdf" (5 MB) onto the drop zone (or use the file picker). | File is accepted. File name and size (5 MB) are displayed. An upload progress bar appears. |
| 4 | Select "Contract" from the Category dropdown. | Category is selected. |
| 5 | Enter "Signed employment contract 2026" in the Description field. | Text is accepted. |
| 6 | Set Expiry Date to 2027-06-12. | Date is selected. |
| 7 | Click "Upload" / "Submit". | Upload progress bar advances to 100%. A success toast appears (e.g., "Document uploaded successfully"). |
| 8 | Verify the API request `POST /api/v1/tenant/employees/{emp-001-uuid}/documents` was sent. | Request is multipart/form-data containing the file, category, description, and expiry_date. Response status is 201 Created. Response body contains `document_id` (UUID), `tenant_id` matching acme, `storage_key` matching `{acme-tenant-id}/core-hr/emp-001-uuid/2026/06/employment-contract.pdf`. |
| 9 | Query the `employee_documents` table for the new record. | Record exists with: `tenant_id` = acme tenant UUID, `employee_id` = emp-001-uuid, `file_name` = "employment-contract.pdf", `mime_type` = "application/pdf", `file_size_bytes` ~ 5242880, `category` = "Contract", `description` = "Signed employment contract 2026", `expiry_date` = 2027-06-12, `uploaded_by` = HR Officer user ID, `is_deleted` = false. |
| 10 | Verify the file exists in object storage at the expected path. | Object storage contains the file at `{acme-tenant-id}/core-hr/emp-001-uuid/2026/06/employment-contract.pdf`. File size matches. |
| 11 | Verify the document appears in the employee's document list on the UI. | The document list shows a row with: file icon (PDF type), file name "employment-contract.pdf", category tag "Contract", upload date (today), size "5 MB", uploader name, expiry badge (green, > 30 days). |

## 6. Postconditions
- A new `employee_documents` record exists with `is_deleted = false` and all metadata populated.
- The PDF file exists in object storage at the tenant-isolated path.
- The document list on the employee profile reflects the new upload.
- Audit columns (`created_at`, `created_by`) are auto-populated.

## 7. Test Category Tags
- [x] Happy path
- [ ] Negative test
- [ ] Boundary test
- [ ] Security test
- [ ] Multi-tenant isolation
- [ ] Performance test
- [ ] Accessibility test
- [ ] Cross-browser test
