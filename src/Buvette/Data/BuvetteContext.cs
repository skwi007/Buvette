using Microsoft.EntityFrameworkCore;

namespace Buvette.Data;

public class BuvetteContext(DbContextOptions<BuvetteContext> options) : DbContext(options)
{
    public DbSet<Evenement> Evenements => Set<Evenement>();
    public DbSet<Produit> Produits => Set<Produit>();
    public DbSet<Commande> Commandes => Set<Commande>();
    public DbSet<LigneCommande> Lignes => Set<LigneCommande>();
    public DbSet<PreCommande> PreCommandes => Set<PreCommande>();
    public DbSet<LignePreCommande> LignesPreCommandes => Set<LignePreCommande>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Evenement>(e =>
        {
            e.HasMany(x => x.Produits)
             .WithOne(x => x.Evenement!)
             .HasForeignKey(x => x.EvenementId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Commandes)
             .WithOne(x => x.Evenement!)
             .HasForeignKey(x => x.EvenementId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.PreCommandes)
             .WithOne(x => x.Evenement!)
             .HasForeignKey(x => x.EvenementId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PreCommande>(e =>
        {
            e.HasMany(x => x.Lignes)
             .WithOne(x => x.PreCommande!)
             .HasForeignKey(x => x.PreCommandeId)
             .OnDelete(DeleteBehavior.Cascade);

            // Resynchroniser met à jour la pré-commande déjà connue au lieu d'en créer une seconde.
            e.HasIndex(x => new { x.EvenementId, x.NumeroHelloAsso }).IsUnique();
        });

        b.Entity<LignePreCommande>()
         .HasOne(x => x.Produit)
         .WithMany()
         .HasForeignKey(x => x.ProduitId)
         .OnDelete(DeleteBehavior.SetNull);

        b.Entity<Commande>(e =>
        {
            e.HasMany(x => x.Lignes)
             .WithOne(x => x.Commande!)
             .HasForeignKey(x => x.CommandeId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => x.EvenementId);
        });

        // Un produit supprimé ne doit pas effacer les ventes déjà encaissées :
        // la ligne garde le nom et le prix recopiés, et perd juste la référence.
        b.Entity<LigneCommande>()
         .HasOne(x => x.Produit)
         .WithMany()
         .HasForeignKey(x => x.ProduitId)
         .OnDelete(DeleteBehavior.SetNull);
    }
}
