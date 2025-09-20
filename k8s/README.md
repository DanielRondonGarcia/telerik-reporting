# Kubernetes Manifests para GenReports API

Este directorio contiene los manifiestos de Kubernetes necesarios para desplegar la API GenReports en un clúster de Kubernetes.

## Archivos incluidos

### 1. `deployment.yaml`
- **Deployment** para la aplicación GenReports API
- Configurado para 2 réplicas
- Expone puertos 8080 (HTTP) y 8081 (HTTPS)
- Incluye health checks (liveness y readiness probes)
- Configurado con límites de recursos
- Volumen para almacenamiento de reportes

### 2. `service.yaml`
- **ClusterIP Service** para comunicación interna
- **NodePort Service** para acceso externo directo
- Mapeo de puertos estándar (80/443 → 8080/8081)

### 3. `configmap.yaml`
- Configuración de la aplicación
- Variables de entorno para ASP.NET Core
- Configuración de logging y Swagger
- Configuración específica de Telerik Reporting

### 4. `ingress.yaml`
- Ingress para exposición externa con NGINX
- Configurado para manejar archivos grandes
- Rutas para API, Swagger y aplicación principal
- Host: `genreports-api.local`

## Comandos de despliegue

### Aplicar todos los manifiestos:
```bash
kubectl apply -f k8s/
```

### Aplicar manifiestos individuales:
```bash
kubectl apply -f k8s/configmap.yaml
kubectl apply -f k8s/deployment.yaml
kubectl apply -f k8s/service.yaml
kubectl apply -f k8s/ingress.yaml
```

### Verificar el despliegue:
```bash
# Ver pods
kubectl get pods -l app=genreports-api

# Ver servicios
kubectl get services -l app=genreports-api

# Ver logs
kubectl logs -l app=genreports-api

# Describir deployment
kubectl describe deployment genreports-api
```

### Acceso a la aplicación:

1. **Vía NodePort**: `http://localhost:30080` o `https://localhost:30443`
2. **Vía Ingress**: `http://genreports-api.local` (requiere configurar /etc/hosts)
3. **Port Forward**: 
   ```bash
   kubectl port-forward service/genreports-api-service 8080:80
   ```

### Configurar host local para Ingress:
Agregar a `/etc/hosts` (Linux/Mac) o `C:\Windows\System32\drivers\etc\hosts` (Windows):
```
127.0.0.1 genreports-api.local
```

## Notas importantes

- La imagen Docker debe estar disponible en `ghcr.io/danielrondongarcia/devsecops:latest`
- Los health checks apuntan a `/health` - asegúrate de que este endpoint exista
- El volumen para reportes usa `emptyDir` - considera usar PersistentVolume para producción
- Los recursos están configurados conservadoramente - ajusta según necesidades

## Troubleshooting

### Si los pods no inician:
```bash
kubectl describe pod <pod-name>
kubectl logs <pod-name>
```

### Si el servicio no responde:
```bash
kubectl get endpoints genreports-api-service
kubectl port-forward pod/<pod-name> 8080:8080
```