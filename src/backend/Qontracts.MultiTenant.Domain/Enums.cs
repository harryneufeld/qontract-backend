namespace Qontracts.MultiTenant.Domain
{
    using System.ComponentModel.DataAnnotations;

    public enum TenantType
    {
        Family = 0,
        Company = 1
    }

    public enum UserRole
    {
        TenantAdmin = 0,
        User = 1,
        Contractor = 2
    }

    public enum ContractorType
    {
        Individual = 0,
        Company = 1
    }

    public enum ContractStatus
    {
        Draft = 0,
        Active = 1,
        Expired = 2,
        Terminated = 3
    }

    public enum EntityType
    {
        Person = 0,
        Company = 1,
        Contractor = 2,
        Tenant = 3
    }
}
