#!/usr/bin/env bash

set -e
set -o pipefail

# Usage: deploy.sh [version] [namespace]
# Example: deploy.sh v1.0.0 default

if [ $# -lt 2 ]; then
    echo "Usage: deploy.sh [version] [namespace]"
    echo "Example: deploy.sh v1.0.0 default"
    exit 1
fi

VERSION=$1
NAMESPACE=$2
SERVICE_NAME="skeleton-api-net"
REGISTRY=${REGISTRY:-"asia.gcr.io/my-project"}

echo "========================================="
echo "Deploying ${SERVICE_NAME}:${VERSION}"
echo "Namespace: ${NAMESPACE}"
echo "========================================="

# Check if we are deploying to Huawei Cloud (GitOps Mode)
if [ -n "$HUAWEI_PROJECT" ]; then
    echo "Detected Huawei Cloud deployment (GitOps Mode)"
    
    # Validation
    if [ -z "$ARGOCD_PROJECT" ] || [ -z "$BRANCH_NAME" ] || [ -z "$CLUSTER_NAME_HUAWEI" ] || [ -z "$NAMESPACE_HUAWEI" ]; then
        echo "Error: Missing required environment variables for Huawei deployment."
        echo "Required: ARGOCD_PROJECT, BRANCH_NAME, CLUSTER_NAME_HUAWEI, NAMESPACE_HUAWEI"
        exit 1
    fi

    IMAGE_HUAWEI="swr.ap-southeast-4.myhuaweicloud.com/$HUAWEI_PROJECT/$SERVICE_NAME:$VERSION"
    IMAGE_HUAWEI_PATH="$ARGOCD_PROJECT/hcloud/$SERVICE_NAME/overlays/$BRANCH_NAME"
    
    echo "Huawei Image: $IMAGE_HUAWEI"
    echo "ArgoCD Path: $IMAGE_HUAWEI_PATH"

    # Clone ArgoCD repository
    if [ ! -d "bbone" ]; then
        echo "Cloning ArgoCD repository..."
        git clone git@gitssh.bluebird.id:argocd/bbone.git
    else
        echo "Updating ArgoCD repository..."
        rm -rf bbone
        git clone git@gitssh.bluebird.id:argocd/bbone.git
    fi

    # Update image in ArgoCD repo
    cd bbone
    git pull
    
    echo "Updating deployment patch..."
    if [ -f "$IMAGE_HUAWEI_PATH/deployment-patch.yaml" ]; then
        sed -i "s#image: .*#image: $IMAGE_HUAWEI#" $IMAGE_HUAWEI_PATH/deployment-patch.yaml
        
        # Commit and push changes
        git config user.email "jenkins@bluebird.id"
        git config user.name "Jenkins Build"
        git add .
        git commit -m "[$SERVICE_NAME] - Update Version To $VERSION"
        git push
    else
        echo "Error: Deployment patch not found at $IMAGE_HUAWEI_PATH/deployment-patch.yaml"
        cd ..
        rm -rf bbone
        exit 1
    fi
    
    cd ..
    rm -rf bbone

    # Apply ArgoCD Application manifest
    echo "Applying ArgoCD Application manifest..."
    
    # Create a temporary copy to avoid modifying the template
    cp deployments/huawei-application.yaml deployments/huawei-application-generated.yaml
    
    sed -i "s#env: .*#env: $BRANCH_NAME#" deployments/huawei-application-generated.yaml
    sed -i "s/project: projectid/project: $ARGOCD_PROJECT/" deployments/huawei-application-generated.yaml
    sed -i "s/namespace: namespace/namespace: $NAMESPACE_HUAWEI/" deployments/huawei-application-generated.yaml
    sed -i "s#path: .*#path: $IMAGE_HUAWEI_PATH#" deployments/huawei-application-generated.yaml
    sed -i "s/name: clustername/name: $CLUSTER_NAME_HUAWEI/" deployments/huawei-application-generated.yaml
    sed -i "s/name: apps_name/name: $BRANCH_NAME-$SERVICE_NAME/" deployments/huawei-application-generated.yaml
    
    # Apply using specific kubeconfig if available, otherwise default
    KUBECONFIG_FLAG=""
    if [ -f "huawei-kubeconfig.conf" ]; then
        KUBECONFIG_FLAG="--kubeconfig=huawei-kubeconfig.conf"
    fi
    
    kubectl apply -f deployments/huawei-application-generated.yaml -n argocd $KUBECONFIG_FLAG
    
    # Cleanup
    rm deployments/huawei-application-generated.yaml
    
    echo "GitOps deployment triggered successfully via ArgoCD!"

else
    # --- Direct Apply Mode (Legacy/Local) ---
    echo "Standard deployment (Direct Apply Mode)"
    
    # Update deployment manifest with new image version
    DEPLOYMENT_FILE="deployments/service.yaml"
    IMAGE="${REGISTRY}/${SERVICE_NAME}:${VERSION}"
    
    echo "Updating image in deployment manifest..."
    sed -i.bak "s|image: ${SERVICE_NAME}:.*|image: ${IMAGE}|g" ${DEPLOYMENT_FILE}
    
    # Apply Kubernetes manifests
    echo "Applying Kubernetes manifests..."
    kubectl apply -f ${DEPLOYMENT_FILE} -n ${NAMESPACE}
    
    # Wait for rollout to complete
    echo "Waiting for deployment rollout..."
    kubectl rollout status deployment/${SERVICE_NAME} -n ${NAMESPACE} --timeout=5m
    
    # Restore original deployment file
    mv ${DEPLOYMENT_FILE}.bak ${DEPLOYMENT_FILE}
    
    # Show deployment status
    echo ""
    echo "Deployment Status:"
    kubectl get deployment ${SERVICE_NAME} -n ${NAMESPACE}
    echo ""
    echo "Pods:"
    kubectl get pods -l app=${SERVICE_NAME} -n ${NAMESPACE}
fi

echo "========================================="
echo "Deployment process completed!"
echo "========================================="
