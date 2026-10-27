# myDock

Un piccolo "widget" per Ubuntu che mostra i container Docker attivi e le risorse che usano:
un'icona nella barra in alto con il numero di container, e un pannello compatto con CPU, RAM,
rete e disco di ciascuno, più i comandi per avviarli, fermarli e riavviarli.

Scritto in .NET 10 con [Avalonia](https://avaloniaui.net/).

## Requisiti

- Ubuntu con GNOME (provato su 26.04, sessione Wayland) e l'estensione AppIndicator, attiva di serie.
- Docker Engine in esecuzione.
- L'utente deve poter leggere `/var/run/docker.sock`:

  ```bash
  sudo usermod -aG docker $USER   # poi esci e rientra nella sessione
  ```

  Chi fa parte del gruppo `docker` ha di fatto i privilegi di root sulla macchina.
- Per compilare: l'SDK .NET 10 (`sudo apt install dotnet-sdk-10.0`). L'app installata non ne ha bisogno.

## Installazione

```bash
packaging/install.sh             # compila e installa per l'utente corrente
packaging/install.sh --uninstall # rimuove tutto, tranne le impostazioni
```

L'app finisce in `~/.local/share/mydock`, con il comando `mydock` in `~/.local/bin` e la voce nel menu
delle applicazioni. Per farla partire all'accesso: menu dell'icona → *Avvia all'accesso*.

## Uso

| Comando | Effetto |
|---|---|
| `mydock` | avvia e mostra il pannello; se è già in esecuzione, lo mostra |
| `mydock --hidden` | avvia solo l'icona nella barra |
| `mydock --toggle` | apre o chiude il pannello dell'istanza in esecuzione |
| `mydock --quit` | chiude l'istanza in esecuzione |
| `mydock --dump` | stampa container e risorse sul terminale ed esce |

Su GNOME il click sull'icona apre il suo menu, la cui prima voce mostra o nasconde il pannello.
Per averlo a portata di tasto, associa `mydock --toggle` a una scorciatoia in
*Impostazioni → Tastiera → Scorciatoie personalizzate*.

Nel pannello:

- il pannello si chiude da solo quando perde il focus; la puntina lo tiene aperto. Si sposta trascinando l'intestazione;
- *Ordine* alterna nome, CPU e RAM. Ordinando per nome, i container di uno stesso progetto Compose sono raggruppati;
- fermare e riavviare chiedono un secondo click di conferma;
- *Fermi* elenca i container non in esecuzione, che si possono riavviare da lì.

La CPU segue la convenzione di `docker stats`: 100% è un core intero. La RAM esclude la cache di pagina recuperabile.
Le statistiche vengono campionate solo mentre il pannello è aperto.

Per un demone diverso da quello locale vale la variabile `DOCKER_HOST`. Le impostazioni stanno in `~/.config/mydock/settings.json`.

## Sviluppo

```bash
dotnet build
dotnet test
dotnet run --project src/MyDock.App
```

Avalonia raccoglie dati d'uso anonimi durante la compilazione (non nell'app installata);
per disattivarli imposta `AVALONIA_TELEMETRY_OPTOUT=1` nell'ambiente.

| Progetto | Contenuto |
|---|---|
| `src/MyDock.Core` | accesso a Docker (`IDockerService`), calcolo delle statistiche, `ContainerMonitor`. Nessuna dipendenza dalla UI |
| `src/MyDock.App` | icona tray, pannello, view model |
| `tests/MyDock.Core.Tests` | test del Core, con un finto servizio Docker |

`ContainerMonitor` tiene aggiornato l'elenco ascoltando gli eventi di Docker, senza interrogarlo a intervalli,
e si ricollega da solo se il demone sparisce.

Il pannello si posiziona in alto a destra perché Avalonia su Linux usa X11 (qui tramite XWayland):
un client Wayland nativo non può scegliere dove comparire.
