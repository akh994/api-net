pipeline {
    agent {
        node {
            label 'slave-01'
            customWorkspace "workspace/${env.BRANCH_NAME}/src/git.bluebird.id/bb-one/skeleton-api-net"
        }
    }
    environment {
        SERVICE_NAME = 'skeleton-api-net'
        ARGOCD_PROJECT = "bbone"
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        NUGET_PACKAGES = "${env.WORKSPACE}/.nuget/packages"
    }
    options {
        buildDiscarder(logRotator(daysToKeepStr: env.BRANCH_NAME == 'main' ? '90' : '30'))
        timestamps()
    }
    stages {
        stage('Checkout') {
            when {
                anyOf { branch 'main'; branch 'develop'; branch 'staging'; branch 'regress' }
            }
            steps {
                echo 'Checking out from Git'
                checkout scm
            }
        }
        stage('Restore Dependencies') {
            agent {
                docker {
                    image 'mcr.microsoft.com/dotnet/sdk:10.0'
                    args "-e HOME=/tmp -e NUGET_PACKAGES=${env.WORKSPACE}/.nuget/packages"
                    reuseNode true
                }
            }
            steps {
                echo 'Restoring NuGet packages...'
                sh 'dotnet restore skeleton-api-net.sln'
            }
        }
        stage('Build') {
            agent {
                docker {
                    image 'mcr.microsoft.com/dotnet/sdk:10.0'
                    args "-e HOME=/tmp -e NUGET_PACKAGES=${env.WORKSPACE}/.nuget/packages"
                    reuseNode true
                }
            }
            steps {
                echo 'Building application...'
                sh 'dotnet build skeleton-api-net.sln -c Release --no-restore'
            }
        }
        stage('Testing') {
            agent {
                docker {
                    image 'mcr.microsoft.com/dotnet/sdk:10.0'
                    args "-e HOME=/tmp -e NUGET_PACKAGES=${env.WORKSPACE}/.nuget/packages"
                    reuseNode true
                }
            }
            steps {
                echo 'Running unit tests...'
                sh '''
                    dotnet test skeleton-api-net.sln -c Release --no-build \
                        --collect:"XPlat Code Coverage" \
                        --results-directory ./coverage \
                        --logger "trx;LogFileName=test-results.trx"
                '''
            }
        }
        stage('Code Review') {
            environment {
                scannerHome = tool 'sonarQubeScanner'
            }
            when {
                anyOf { branch 'main'; branch 'develop'; branch 'staging'; branch 'regress' }
            }
            steps {
                withSonarQubeEnv('sonarQube') {
                    sh """
                        ${scannerHome}/bin/sonar-scanner \
                            -Dproject.settings=sonar-project.properties
                    """
                }
            }
        }
        stage('Prepare') {
            steps {
                withCredentials([file(credentialsId: '3521ab7f-3916-4e56-a41e-c0dedd2e98e9', variable: 'sa')]) {
                    sh "cp $sa service-account.json"
                    sh "chmod 644 service-account.json"
                    sh "docker login -u _json_key --password-stdin https://asia.gcr.io < service-account.json"
                    sh "rm -f service-account.json"
                }
            }
        }
        stage('Build and Deploy') {
            environment {
                VERSION_PREFIX = '1.0'
            }
            stages {
                stage('Deploy to development') {
                    when {
                        branch 'develop'
                    }
                    environment {
                        VERSION = "${env.VERSION_PREFIX}-dev${env.BUILD_NUMBER}"
                        BRANCH_NAME = "develop"
                        CONFIG = "appsettings.Development.json"

                        HUAWEI_PROJECT = "bbone_dev_bluebirdgroup"
                        CLUSTER_NAME_HUAWEI = "cce_huawei_bbone_dev_bluebirdgroup_bbone_dev-internal-cluster"
                        NAMESPACE_HUAWEI = "bb-one"
                    }
                    steps {
                        withCredentials([
                            file(credentialsId: 'a5c372d1-9105-4d45-b7f1-a6f5f1acfb12', variable: 'gkubeconfig'),
                            file(credentialsId: '1ffe0701-2c4a-4497-8c3f-9f941a382e3a', variable: 'hkubeconfig')
                        ]) {
                            sh "cp $gkubeconfig gcp-kubeconfig.conf"
                            sh "cp $hkubeconfig huawei-kubeconfig.conf"
                            sh "chmod 644 gcp-kubeconfig.conf"
                            sh "chmod 644 huawei-kubeconfig.conf"
                            sh 'chmod +x build.sh'
                            sh './build.sh $VERSION $CONFIG'
                            sh 'chmod +x deploy.sh'
                            sh './deploy.sh $VERSION $NAMESPACE_HUAWEI'
                        }
                    }
                }
                stage('Deploy to staging') {
                    when {
                        branch 'staging'
                    }
                    environment {
                        VERSION = "${env.VERSION_PREFIX}-stg${env.BUILD_NUMBER}"
                        BRANCH_NAME = "staging"
                        CONFIG = "appsettings.Staging.json"

                        HUAWEI_PROJECT = "bbone_dev_bluebirdgroup"
                        CLUSTER_NAME_HUAWEI = "cce_huawei_bbone_dev_bluebirdgroup_bbone_dev-internal-cluster"
                        NAMESPACE_HUAWEI = "bb-one-staging"
                    }
                    steps {
                        withCredentials([
                            file(credentialsId: 'a5c372d1-9105-4d45-b7f1-a6f5f1acfb12', variable: 'gkubeconfig'),
                            file(credentialsId: '1ffe0701-2c4a-4497-8c3f-9f941a382e3a', variable: 'hkubeconfig')
                        ]) {
                            sh "cp $gkubeconfig gcp-kubeconfig.conf"
                            sh "cp $hkubeconfig huawei-kubeconfig.conf"
                            sh "chmod 644 gcp-kubeconfig.conf"
                            sh "chmod 644 huawei-kubeconfig.conf"
                            sh 'chmod +x build.sh'
                            sh './build.sh $VERSION $CONFIG'
                            sh 'chmod +x deploy.sh'
                            sh './deploy.sh $VERSION $NAMESPACE_HUAWEI'
                        }
                    }
                }
                stage('Deploy to regress') {
                    when {
                        branch 'regress'
                    }
                    environment {
                        VERSION = "${env.VERSION_PREFIX}-rgr${env.BUILD_NUMBER}"
                        BRANCH_NAME = "regress"
                        CONFIG = "appsettings.Regress.json"

                        HUAWEI_PROJECT = "bbone_dev_bluebirdgroup"
                        CLUSTER_NAME_HUAWEI = "cce_huawei_bbone_dev_bluebirdgroup_bbone_dev-internal-cluster"
                        NAMESPACE_HUAWEI = "bb-one-regress"
                    }
                    steps {
                        withCredentials([
                            file(credentialsId: 'a5c372d1-9105-4d45-b7f1-a6f5f1acfb12', variable: 'gkubeconfig'),
                            file(credentialsId: '1ffe0701-2c4a-4497-8c3f-9f941a382e3a', variable: 'hkubeconfig')
                        ]) {
                            sh "cp $gkubeconfig gcp-kubeconfig.conf"
                            sh "cp $hkubeconfig huawei-kubeconfig.conf"
                            sh "chmod 644 gcp-kubeconfig.conf"
                            sh "chmod 644 huawei-kubeconfig.conf"
                            sh 'chmod +x build.sh'
                            sh './build.sh $VERSION $CONFIG'
                            sh 'chmod +x deploy.sh'
                            sh './deploy.sh $VERSION $NAMESPACE_HUAWEI'
                        }
                    }
                }
                stage('Deploy to production') {
                    when {
                        branch 'v*'
                    }
                    environment {
                        VERSION = "${env.VERSION_PREFIX}-prd${env.TAG_NAME}"
                        BRANCH_NAME = "main"
                        CONFIG = "appsettings.Production.json"

                        HUAWEI_PROJECT = "bbone_prd_bluebirdgroup"
                        CLUSTER_NAME_HUAWEI = "cce_huawei_bbone_prd_bluebirdgroup_bbone_prd-internal-cluster"
                        NAMESPACE_HUAWEI = "bb-one"
                    }
                    steps {
                        withCredentials([
                            file(credentialsId: 'a5c372d1-9105-4d45-b7f1-a6f5f1acfb12', variable: 'gkubeconfig'),
                            file(credentialsId: '1ffe0701-2c4a-4497-8c3f-9f941a382e3a', variable: 'hkubeconfig')
                        ]) {
                            sh "cp $gkubeconfig gcp-kubeconfig.conf"
                            sh "cp $hkubeconfig huawei-kubeconfig.conf"
                            sh "chmod 644 gcp-kubeconfig.conf"
                            sh "chmod 644 huawei-kubeconfig.conf"
                            sh 'chmod +x build.sh'
                            sh './build.sh $VERSION $CONFIG'
                            sh 'chmod +x deploy.sh'
                            sh './deploy.sh $VERSION $NAMESPACE_HUAWEI'
                        }
                    }
                }
            }
        }
    }
    post {
        success {
            slackSend color: '#00FF00', message: "Job ${env.JOB_NAME} build ${env.BUILD_NUMBER} SUCCESS (<${env.BUILD_URL}|Open>)"
        }
        failure {
            slackSend color: '#FF0000', message: "Job ${env.JOB_NAME} build ${env.BUILD_NUMBER} FAILED (<${env.BUILD_URL}|Open>)"
        }
    }
}