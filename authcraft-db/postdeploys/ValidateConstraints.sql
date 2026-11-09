ALTER TABLE [dbo].[invitations] WITH CHECK CHECK CONSTRAINT [FK_invitations_accepted_identity];
ALTER TABLE [dbo].[invitations] WITH CHECK CHECK CONSTRAINT [CK_invitations_purpose];
ALTER TABLE [dbo].[invitations] WITH CHECK CHECK CONSTRAINT [CK_invitations_registered_dni];
ALTER TABLE [dbo].[invitations] WITH CHECK CHECK CONSTRAINT [CK_invitations_student_dni];
ALTER TABLE [dbo].[invitations] WITH CHECK CHECK CONSTRAINT [CK_invitations_student_profile];
ALTER TABLE [dbo].[memberships] WITH CHECK CHECK CONSTRAINT [CK_memberships_status];
ALTER TABLE [dbo].[tenants] WITH CHECK CHECK CONSTRAINT [CK_tenants_status];

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE schema_id = SCHEMA_ID(N'dbo')
        AND name IN (
            N'CK_invitations_purpose', N'CK_invitations_registered_dni',
            N'CK_invitations_student_dni', N'CK_invitations_student_profile',
            N'CK_memberships_status', N'CK_tenants_status')
        AND (is_disabled = 1 OR is_not_trusted = 1))
    OR EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.invitations')
            AND name = N'FK_invitations_accepted_identity'
            AND (is_disabled = 1 OR is_not_trusted = 1))
BEGIN
    THROW 51000, 'Reviewed deployment constraints must be enabled and trusted.', 1;
END;
